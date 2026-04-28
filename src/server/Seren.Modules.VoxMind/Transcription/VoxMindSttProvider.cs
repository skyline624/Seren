using System.Buffers.Binary;
using System.Diagnostics;
using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Seren.Application.Abstractions;
using Seren.Modules.VoxMind.Diagnostics;
using Seren.Modules.VoxMind.Parakeet;

namespace Seren.Modules.VoxMind.Transcription;

/// <summary>
/// VoxMind STT provider — runs the Parakeet TDT 0.6B v3 ONNX bundle locally
/// (no Python dependency). Falls back to an empty result when the model
/// directory is unset or incomplete, so the voice flow stays functional in
/// development environments without the multi-GB model bundle on disk.
/// </summary>
/// <remarks>
/// Required files in <see cref="VoxMindSttOptions.ModelDir"/>:
/// <list type="bullet">
///   <item><c>nemo128.onnx</c> — mel spectrogram preprocessor.</item>
///   <item><c>encoder-model.int8.onnx</c> — Parakeet encoder.</item>
///   <item><c>decoder_joint-model.int8.onnx</c> — TDT decoder/joint.</item>
///   <item><c>vocab.txt</c> — vocabulary.</item>
/// </list>
/// ONNX inference sessions are not safe under concurrent <c>Run</c> calls, so
/// transcription is serialised through a <see cref="SemaphoreSlim"/>. The
/// post-hoc <see cref="ILanguageDetector"/> infers the spoken language from
/// the transcribed text — Parakeet v3 supports 25 European tongues and
/// transcribes faithfully but does not surface a language code.
/// </remarks>
public sealed class VoxMindSttProvider : ISttProvider, IDisposable
{
    /// <summary>
    /// ISO 639-1 codes supported by Parakeet TDT v3 (per NVIDIA model card).
    /// The detector is bounded to this set so a short transcription cannot be
    /// misclassified to a language outside the engine's supported range.
    /// </summary>
    internal static readonly IReadOnlyList<string> ParakeetSupportedLanguages =
    [
        "bg", "hr", "cs", "da", "nl", "en", "et", "fi", "fr", "de",
        "el", "hu", "it", "lv", "lt", "mt", "pl", "pt", "ro", "sk",
        "sl", "es", "sv", "ru", "uk",
    ];

    private readonly VoxMindOptions _options;
    private readonly ILogger<VoxMindSttProvider> _logger;
    private readonly ILanguageDetector _languageDetector;
    private readonly VoxMindMetrics _metrics;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    private readonly Lock _initGate = new();
    private bool _initialised;
    private bool _modelsAvailable;
    private AudioPreprocessor? _preprocessor;
    private ParakeetEncoder? _encoder;
    private ParakeetDecoderJoint? _decoder;
    private TokenDecoder? _tokenDecoder;
    private bool _disposed;

    public VoxMindSttProvider(
        IOptions<VoxMindOptions> options,
        ILogger<VoxMindSttProvider> logger,
        ILanguageDetector languageDetector,
        VoxMindMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(languageDetector);
        ArgumentNullException.ThrowIfNull(metrics);
        _options = options.Value;
        _logger = logger;
        _languageDetector = languageDetector;
        _metrics = metrics;
    }

    /// <summary>True once the Parakeet ONNX bundle has been loaded (test-friendly probe).</summary>
    internal bool ModelsAvailable
    {
        get { EnsureInitialised(); return _modelsAvailable; }
    }

    /// <inheritdoc />
    public async Task<SttResult> TranscribeAsync(byte[] audioData, string format, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audioData);
        ArgumentNullException.ThrowIfNull(format);

        _metrics.SttRequests.Add(1);
        var sw = Stopwatch.StartNew();
        try
        {
            return await TranscribeCoreAsync(audioData, format, ct).ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            _metrics.SttDurationMs.Record(sw.Elapsed.TotalMilliseconds);
        }
    }

    private async Task<SttResult> TranscribeCoreAsync(byte[] audioData, string format, CancellationToken ct)
    {
        if (audioData.Length == 0)
        {
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        EnsureInitialised();
        if (!_modelsAvailable)
        {
            _logger.LogDebug(
                "VoxMind STT: skipping transcription — Parakeet model directory not configured.");
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        float[] samples;
        try
        {
            samples = await DecodeToFloat32Async(audioData, format, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "VoxMind STT: failed to decode {Format} audio ({Bytes} bytes).", format, audioData.Length);
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        if (samples.Length == 0)
        {
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        await _inferenceLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var text = await Task.Run(() => RunInference(samples), ct).ConfigureAwait(false);
            var lang = ResolveLanguage(text);
            var confidence = text.Length > 0 ? 0.9f : 0f;
            return new SttResult(text, lang, confidence);
        }
        catch (OperationCanceledException)
        {
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VoxMind STT: Parakeet inference failed.");
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

            var dir = _options.Stt.ModelDir;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                _logger.LogInformation(
                    "VoxMind STT disabled — Modules:VoxMind:Stt:ModelDir not set or missing ({Dir}).",
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
                    "VoxMind STT: Parakeet ONNX bundle loaded from {Dir} ({Vocab} tokens).",
                    dir, _tokenDecoder.VocabSize);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "VoxMind STT: failed to load Parakeet bundle from {Dir} — STT disabled.", dir);
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
            return string.Empty;
        }

        var (encoded, encodedFrames, hiddenDim) = _encoder!.Encode(mel, melFrames);
        if (encodedFrames == 0)
        {
            return string.Empty;
        }

        int[] tokenIds = _decoder!.DecodeGreedy(encoded, encodedFrames, hiddenDim);
        return _tokenDecoder!.DecodeTokens(tokenIds).Trim();
    }

    /// <summary>
    /// Decodes any input audio blob to PCM float32 16 kHz mono.
    /// Pure-WAV inputs skip the FFmpeg detour.
    /// </summary>
    private static async Task<float[]> DecodeToFloat32Async(byte[] audioData, string format, CancellationToken ct)
    {
        var fmt = format.Trim().ToLowerInvariant();
        if (fmt is "wav" or "wave")
        {
            return ConvertWavToFloat32(audioData);
        }

        using var input = new MemoryStream(audioData, writable: false);
        using var output = new MemoryStream();

        await FFMpegArguments
            .FromPipeInput(new StreamPipeSource(input))
            .OutputToPipe(new StreamPipeSink(output), opts => opts
                .WithAudioSamplingRate(16000)
                .WithCustomArgument("-ac 1 -acodec pcm_s16le")
                .ForceFormat("wav"))
            .CancellableThrough(ct)
            .ProcessAsynchronously(throwOnError: true).ConfigureAwait(false);

        return ConvertWavToFloat32(output.ToArray());
    }

    /// <summary>
    /// Converts a PCM-16 WAV blob into float32 samples normalised to [-1, 1].
    /// Tolerant to extra chunks before "data".
    /// </summary>
    private static float[] ConvertWavToFloat32(byte[] wav)
    {
        if (wav.Length < 44)
        {
            return Array.Empty<float>();
        }

        int dataOffset = 44;
        for (int i = 12; i < Math.Min(wav.Length - 8, 1024); i++)
        {
            if (wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a')
            {
                dataOffset = i + 8;
                break;
            }
        }

        int nSamples = (wav.Length - dataOffset) / 2;
        if (nSamples <= 0)
        {
            return Array.Empty<float>();
        }

        var samples = new float[nSamples];
        for (int i = 0; i < nSamples; i++)
        {
            short s = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(dataOffset + i * 2, 2));
            samples[i] = s / 32768.0f;
        }
        return samples;
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
