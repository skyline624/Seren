using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Modules.VoxMind.Audio;
using SherpaOnnx;

namespace Seren.Modules.VoxMind.Transcription.Engines;

/// <summary>
/// Whisper STT engine via the sherpa-onnx <see cref="OfflineRecognizer"/>.
/// Significantly better quality than Parakeet on French (and on accents,
/// long sentences, conjugaisons silencieuses), at the cost of higher
/// latency on CPU. Recommended default for users speaking FR, IT, ES, DE.
/// </summary>
/// <remarks>
/// <para>
/// Multi-variant: the engine maintains a per-size recognizer cache.
/// When the router asks for <c>whisper-tiny</c> the engine looks up
/// <c>{Stt:Whisper:RootDir}/whisper-tiny/</c>, lazy-loads the bundle on
/// first use, and reuses the recognizer thereafter. Switching from one
/// variant to another at runtime keeps both warm.
/// </para>
/// <para>
/// Required files in the variant install directory (sherpa-onnx export
/// naming convention, <c>{size}</c> = <c>tiny|base|small|medium|large</c>):
/// <list type="bullet">
///   <item><c>{size}-encoder.int8.onnx</c></item>
///   <item><c>{size}-decoder.int8.onnx</c></item>
///   <item><c>{size}-tokens.txt</c></item>
/// </list>
/// </para>
/// </remarks>
public sealed class WhisperSttEngine : IVoxMindSttEngine, IVoxMindVariantAwareEngine, IDisposable
{
    private readonly VoxMindOptions _options;
    private readonly ILogger<WhisperSttEngine> _logger;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private readonly ConcurrentDictionary<string, RecognizerSlot> _slots = new(StringComparer.Ordinal);
    private bool _disposed;

    public WhisperSttEngine(
        IOptions<VoxMindOptions> options,
        ILogger<WhisperSttEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "whisper";

    /// <inheritdoc />
    public bool IsAvailable => HasAnyVariantOnDisk();

    /// <summary>
    /// Variant-aware transcription with optional language hint.
    /// </summary>
    /// <param name="audioData">Raw audio bytes.</param>
    /// <param name="format">Audio format (e.g. "wav", "mp3", "ogg", "webm").</param>
    /// <param name="size">
    /// Whisper variant size token (<c>"tiny"</c>, <c>"base"</c>,
    /// <c>"small"</c>, <c>"medium"</c>, <c>"large"</c>) — without the
    /// <c>"whisper-"</c> prefix.
    /// </param>
    /// <param name="language">
    /// ISO 639-1 code to force at decode time (<c>"fr"</c>, <c>"en"</c>,
    /// …) or <c>null</c> / empty to fall back to
    /// <see cref="WhisperEngineOptions.Language"/> (then auto-detect when
    /// that is also empty). Each (size, language) pair has its own
    /// recognizer cached lazily.
    /// </param>
    public Task<SttResult> TranscribeAsync(
        byte[] audioData,
        string format,
        string size,
        string? language,
        CancellationToken ct = default)
        => TranscribeInternalAsync(audioData, format, size, language, ct);

    /// <summary>
    /// Variant-aware transcription with the engine's configured default
    /// language. Kept for backward-compat with callers that haven't been
    /// updated to pass a language hint.
    /// </summary>
    public Task<SttResult> TranscribeAsync(
        byte[] audioData, string format, string size, CancellationToken ct = default)
        => TranscribeInternalAsync(audioData, format, size, language: null, ct);

    /// <inheritdoc />
    public Task<SttResult> TranscribeAsync(byte[] audioData, string format, CancellationToken ct = default)
        => TranscribeInternalAsync(audioData, format, _options.Stt.Whisper.ModelSize, language: null, ct);

    private async Task<SttResult> TranscribeInternalAsync(
        byte[] audioData,
        string format,
        string size,
        string? language,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(audioData);
        ArgumentNullException.ThrowIfNull(format);

        if (string.IsNullOrWhiteSpace(size))
        {
            size = _options.Stt.Whisper.ModelSize;
        }

        var resolvedLanguage = ResolveLanguage(language);

        if (audioData.Length == 0)
        {
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        var recognizer = GetOrLoad(size, resolvedLanguage);
        if (recognizer is null)
        {
            _logger.LogDebug(
                "Whisper engine: variant '{Size}' (lang={Lang}) bundle not on disk — returning empty.",
                size, FormatLangForLog(resolvedLanguage));
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
                "Whisper engine: failed to decode {Format} audio ({Bytes} bytes).", format, audioData.Length);
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        _logger.LogInformation(
            "Whisper engine: decoded {Bytes} {Format} bytes -> {Samples} PCM samples ({Seconds:F2}s @ 16kHz, variant={Size}, lang={Lang}).",
            audioData.Length, format, samples.Length, samples.Length / 16000.0, size, FormatLangForLog(resolvedLanguage));

        if (samples.Length == 0)
        {
            _logger.LogWarning(
                "Whisper engine: decoded audio is empty — likely a malformed WAV or 0-length payload.");
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        await _inferenceLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => RunInference(recognizer, samples, resolvedLanguage), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Whisper engine: inference failed.");
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    /// <summary>
    /// Picks the language that's actually baked into the recognizer
    /// config: caller hint > <see cref="WhisperEngineOptions.Language"/>
    /// > empty (sherpa-onnx auto-detect). Returns the empty string for
    /// auto so we never write null into the slot key.
    /// </summary>
    private string ResolveLanguage(string? hint)
    {
        if (!string.IsNullOrWhiteSpace(hint))
        {
            return hint.Trim().ToLowerInvariant();
        }

        return _options.Stt.Whisper.Language?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static string FormatLangForLog(string lang)
        => string.IsNullOrEmpty(lang) ? "auto" : lang;

    private SttResult RunInference(OfflineRecognizer recognizer, float[] samples, string resolvedLanguage)
    {
        using var stream = recognizer.CreateStream();
        stream.AcceptWaveform(16000, samples);
        recognizer.Decode(stream);

        var result = stream.Result;
        var text = result.Text?.Trim() ?? string.Empty;
        // sherpa-onnx OfflineRecognizerResult does not expose the detected
        // language directly. Surface either the language we forced into
        // the recognizer config (so the rest of the pipeline routes TTS
        // and emotion accordingly) or the module default for auto-detect.
        var lang = !string.IsNullOrEmpty(resolvedLanguage)
            ? resolvedLanguage
            : _options.DefaultLanguage;
        var confidence = text.Length > 0 ? 0.9f : 0f;

        _logger.LogInformation(
            "Whisper engine: inference produced {Chars} chars (lang={Lang}).",
            text.Length, lang);

        return new SttResult(text, lang, confidence);
    }

    private OfflineRecognizer? GetOrLoad(string size, string language)
    {
        var key = BuildSlotKey(size, language);
        var slot = _slots.GetOrAdd(key, _ => new RecognizerSlot());
        slot.EnsureLoaded(this, size, language);
        return slot.Recognizer;
    }

    private static string BuildSlotKey(string size, string language)
        => $"{size}|{language}";

    private bool HasAnyVariantOnDisk()
    {
        // Honour explicit legacy ModelDir first so deployments that pinned
        // a specific bundle path keep showing up as "available".
        var legacy = _options.Stt.Whisper.ModelDir;
        if (!string.IsNullOrWhiteSpace(legacy) && Directory.Exists(legacy))
        {
            var size = _options.Stt.Whisper.ModelSize;
            return File.Exists(Path.Combine(legacy, $"{size}-encoder.int8.onnx"))
                && File.Exists(Path.Combine(legacy, $"{size}-decoder.int8.onnx"))
                && File.Exists(Path.Combine(legacy, $"{size}-tokens.txt"));
        }

        var root = _options.Stt.Whisper.RootDir;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return false;
        }

        // Cheap probe across the 5 known sizes.
        ReadOnlySpan<string> sizes = ["tiny", "base", "small", "medium", "large"];
        foreach (var s in sizes)
        {
            var dir = Path.Combine(root, $"whisper-{s}");
            if (Directory.Exists(dir)
                && File.Exists(Path.Combine(dir, $"{s}-encoder.int8.onnx"))
                && File.Exists(Path.Combine(dir, $"{s}-decoder.int8.onnx"))
                && File.Exists(Path.Combine(dir, $"{s}-tokens.txt")))
            {
                return true;
            }
        }

        return false;
    }

    private string? ResolveBundleDir(string size)
    {
        // Legacy ModelDir wins for the configured size only — keeps the
        // backward-compat path predictable.
        if (!string.IsNullOrWhiteSpace(_options.Stt.Whisper.ModelDir)
            && string.Equals(size, _options.Stt.Whisper.ModelSize, StringComparison.Ordinal))
        {
            return _options.Stt.Whisper.ModelDir;
        }

        var root = _options.Stt.Whisper.RootDir;
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        return Path.Combine(root, $"whisper-{size}");
    }

    private OfflineRecognizer? LoadBundle(string size, string language)
    {
        var dir = ResolveBundleDir(size);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            _logger.LogInformation(
                "Whisper engine: variant '{Size}' bundle directory missing ({Dir}).", size, dir);
            return null;
        }

        var encoderPath = Path.Combine(dir, $"{size}-encoder.int8.onnx");
        var decoderPath = Path.Combine(dir, $"{size}-decoder.int8.onnx");
        var tokensPath = Path.Combine(dir, $"{size}-tokens.txt");

        if (!File.Exists(encoderPath) || !File.Exists(decoderPath) || !File.Exists(tokensPath))
        {
            _logger.LogWarning(
                "Whisper engine: variant '{Size}' bundle in {Dir} is incomplete — files missing.",
                size, dir);
            return null;
        }

        try
        {
            var config = new OfflineRecognizerConfig();
            config.ModelConfig.Whisper.Encoder = encoderPath;
            config.ModelConfig.Whisper.Decoder = decoderPath;
            // sherpa-onnx fixes the decode language at config time. We
            // therefore cache one recognizer per (size, language) pair —
            // the slot key already includes the language so two recognisers
            // for the same variant but different languages don't collide.
            config.ModelConfig.Whisper.Language = language;
            config.ModelConfig.Whisper.Task = "transcribe";
            config.ModelConfig.Tokens = tokensPath;
            config.ModelConfig.NumThreads = 1;
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.Debug = 0;

            var recognizer = new OfflineRecognizer(config);
            _logger.LogInformation(
                "Whisper engine: bundle loaded from {Dir} (size={Size}, lang={Lang}).",
                dir, size, FormatLangForLog(language));
            return recognizer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Whisper engine: failed to load variant '{Size}' from {Dir}.", size, dir);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inferenceLock.Wait(TimeSpan.FromSeconds(5));
        try
        {
            foreach (var slot in _slots.Values)
            {
                (slot.Recognizer as IDisposable)?.Dispose();
            }

            _slots.Clear();
        }
        finally
        {
            _inferenceLock.Release();
            _inferenceLock.Dispose();
        }
    }

    /// <summary>
    /// Per-(size, language) slot that synchronises bundle load attempts.
    /// </summary>
    private sealed class RecognizerSlot
    {
        private readonly Lock _gate = new();
        private bool _attempted;
        public OfflineRecognizer? Recognizer { get; private set; }

        public void EnsureLoaded(WhisperSttEngine owner, string size, string language)
        {
            if (_attempted)
            {
                return;
            }

            lock (_gate)
            {
                if (_attempted)
                {
                    return;
                }

                Recognizer = owner.LoadBundle(size, language);
                _attempted = true;
            }
        }
    }
}
