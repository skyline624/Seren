using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Seren.Application.Abstractions;
using Seren.Modules.VoxMind.Audio;
using Seren.Modules.VoxMind.Parakeet;

namespace Seren.Modules.VoxMind.Transcription.Engines;

/// <summary>
/// Parakeet TDT 0.6B v3 INT8 engine — runs the NVIDIA NeMo ONNX bundle
/// locally (no Python dependency). Lower latency than Whisper, supports 25
/// European languages, but English-biased on transcription quality. Best for
/// low-latency voice-to-LLM where minor errors are tolerable.
/// </summary>
/// <remarks>
/// Required files in <see cref="ParakeetEngineOptions.ModelDir"/>:
/// <list type="bullet">
///   <item><c>nemo128.onnx</c> — mel spectrogram preprocessor.</item>
///   <item><c>encoder-model.int8.onnx</c> — Parakeet encoder.</item>
///   <item><c>decoder_joint-model.int8.onnx</c> — TDT decoder/joint.</item>
///   <item><c>vocab.txt</c> — vocabulary.</item>
/// </list>
/// ONNX inference sessions are not safe under concurrent <c>Run</c> calls, so
/// this engine serialises transcription through a <see cref="SemaphoreSlim"/>.
/// The post-hoc <see cref="ILanguageDetector"/> infers the spoken language
/// from the transcribed text — Parakeet v3 transcribes faithfully but does
/// not surface a language code.
/// </remarks>
public sealed class ParakeetSttEngine : IVoxMindSttEngine, IDisposable
{
    /// <summary>
    /// ISO 639-1 codes supported by Parakeet TDT v3 (per NVIDIA model card).
    /// The detector is bounded to this set so a short transcription cannot
    /// be misclassified to a language outside the engine's supported range.
    /// </summary>
    internal static readonly IReadOnlyList<string> ParakeetSupportedLanguages =
    [
        "bg", "hr", "cs", "da", "nl", "en", "et", "fi", "fr", "de",
        "el", "hu", "it", "lv", "lt", "mt", "pl", "pt", "ro", "sk",
        "sl", "es", "sv", "ru", "uk",
    ];

    private readonly VoxMindOptions _options;
    private readonly ILogger<ParakeetSttEngine> _logger;
    private readonly ILanguageDetector _languageDetector;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    private readonly Lock _initGate = new();
    private bool _initialised;
    private bool _modelsAvailable;
    private AudioPreprocessor? _preprocessor;
    private ParakeetEncoder? _encoder;
    private ParakeetDecoderJoint? _decoder;
    private TokenDecoder? _tokenDecoder;
    private bool _disposed;

    public ParakeetSttEngine(
        IOptions<VoxMindOptions> options,
        ILogger<ParakeetSttEngine> logger,
        ILanguageDetector languageDetector)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(languageDetector);
        _options = options.Value;
        _logger = logger;
        _languageDetector = languageDetector;
    }

    /// <inheritdoc />
    public string Name => "parakeet";

    /// <inheritdoc />
    public bool IsAvailable
    {
        get { EnsureInitialised(); return _modelsAvailable; }
    }

    /// <inheritdoc />
    public async Task<SttResult> TranscribeAsync(byte[] audioData, string format, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audioData);
        ArgumentNullException.ThrowIfNull(format);

        if (audioData.Length == 0)
        {
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        EnsureInitialised();
        if (!_modelsAvailable)
        {
            _logger.LogDebug(
                "Parakeet engine: skipping transcription — model directory not configured.");
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        float[] samples;
        try
        {
            samples = await AudioDecoder.DecodeToFloat32Async(audioData, format, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Parakeet engine: failed to decode {Format} audio ({Bytes} bytes).", format, audioData.Length);
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        _logger.LogInformation(
            "Parakeet engine: decoded {Bytes} {Format} bytes -> {Samples} PCM samples ({Seconds:F2}s @ 16kHz).",
            audioData.Length, format, samples.Length, samples.Length / 16000.0);

        if (samples.Length == 0)
        {
            _logger.LogWarning(
                "Parakeet engine: decoded audio is empty — likely a malformed WAV or 0-length payload.");
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        await _inferenceLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var text = await Task.Run(() => RunInference(samples), ct).ConfigureAwait(false);
            var lang = ResolveLanguage(text);
            var confidence = text.Length > 0 ? 0.9f : 0f;
            _logger.LogInformation(
                "Parakeet engine: inference produced {Chars} chars (lang={Lang}).",
                text.Length, lang);
            return new SttResult(text, lang, confidence);
        }
        catch (OperationCanceledException)
        {
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Parakeet engine: inference failed.");
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    private string ResolveLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return _options.DefaultLanguage;
        }

        var detected = _languageDetector.DetectLanguage(text, ParakeetSupportedLanguages);
        return string.Equals(detected, "und", StringComparison.Ordinal)
            ? _options.DefaultLanguage
            : detected;
    }

    private void EnsureInitialised()
    {
        if (_initialised)
        {
            return;
        }

        lock (_initGate)
        {
            if (_initialised)
            {
                return;
            }

            _initialised = true;

            var dir = _options.Stt.Parakeet.ModelDir;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                _logger.LogInformation(
                    "Parakeet engine disabled — Modules:voxmind:Stt:Parakeet:ModelDir not set or missing ({Dir}).",
                    dir);
                return;
            }

            try
            {
                var opts = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                };

                _tokenDecoder = new TokenDecoder(Path.Combine(dir, "vocab.txt"));
                _preprocessor = new AudioPreprocessor(Path.Combine(dir, "nemo128.onnx"), opts);
                _encoder = new ParakeetEncoder(Path.Combine(dir, "encoder-model.int8.onnx"), opts);
                _decoder = new ParakeetDecoderJoint(
                    Path.Combine(dir, "decoder_joint-model.int8.onnx"),
                    _tokenDecoder,
                    opts);

                _modelsAvailable = true;
                _logger.LogInformation(
                    "Parakeet engine: ONNX bundle loaded from {Dir} ({Vocab} tokens).",
                    dir, _tokenDecoder.VocabSize);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Parakeet engine: failed to load bundle from {Dir} — engine disabled.", dir);
                DisposeComponents();
                _modelsAvailable = false;
            }
        }
    }

    private string RunInference(float[] samples)
    {
        if (samples.Length == 0)
        {
            return string.Empty;
        }

        // Chunk if the segment exceeds the configured limit (Parakeet TDT
        // becomes unstable above ~20 s on CPU; default cap = 12 s).
        int maxSamples = (int)(_options.Stt.MaxChunkSeconds * 16000);
        if (samples.Length <= maxSamples)
        {
            return InferOne(samples);
        }

        var sb = new System.Text.StringBuilder();
        int offset = 0;
        while (offset < samples.Length)
        {
            int len = Math.Min(maxSamples, samples.Length - offset);
            var chunk = new float[len];
            Array.Copy(samples, offset, chunk, 0, len);
            var part = InferOne(chunk);
            if (!string.IsNullOrWhiteSpace(part))
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(part.Trim());
            }
            offset += len;
        }
        return sb.ToString();
    }

    private string InferOne(float[] samples)
    {
        var (mel, melFrames) = _preprocessor!.ComputeMelSpectrogram(samples);
        if (melFrames == 0)
        {
            _logger.LogWarning("Parakeet engine: mel preprocessor returned 0 frames for {N} samples.", samples.Length);
            return string.Empty;
        }

        var (encoded, encodedFrames, hiddenDim) = _encoder!.Encode(mel, melFrames);
        if (encodedFrames == 0)
        {
            _logger.LogWarning("Parakeet engine: encoder returned 0 frames (mel={Mel}).", melFrames);
            return string.Empty;
        }

        int[] tokenIds = _decoder!.DecodeGreedy(encoded, encodedFrames, hiddenDim);
        var text = _tokenDecoder!.DecodeTokens(tokenIds).Trim();
        _logger.LogInformation(
            "Parakeet engine: chunk {Samples} samples -> mel={Mel} frames -> encoded={Enc} frames -> {Tokens} tokens -> '{Text}'.",
            samples.Length, melFrames, encodedFrames, tokenIds.Length, text);
        return text;
    }

    private void DisposeComponents()
    {
        _preprocessor?.Dispose();
        _encoder?.Dispose();
        _decoder?.Dispose();
        _preprocessor = null;
        _encoder = null;
        _decoder = null;
        _tokenDecoder = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Drain any in-flight inference (5 s timeout) before disposing native
        // ONNX sessions — releases native handles cleanly even if a long
        // synthesis is still running.
        _inferenceLock.Wait(TimeSpan.FromSeconds(5));
        try
        {
            DisposeComponents();
        }
        finally
        {
            _inferenceLock.Release();
            _inferenceLock.Dispose();
        }
    }
}
