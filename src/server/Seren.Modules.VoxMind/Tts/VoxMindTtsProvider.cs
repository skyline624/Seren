using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Modules.VoxMind.Audio;
using Seren.Modules.VoxMind.Diagnostics;
using Seren.Modules.VoxMind.F5Tts;

namespace Seren.Modules.VoxMind.Tts;

/// <summary>
/// VoxMind TTS provider — runs F5-TTS ONNX checkpoints locally with one
/// resident engine per language (LRU cache). Yields a single WAV chunk per
/// synthesis call. When no checkpoint matches the requested language and
/// none of the configured languages exist on disk, the provider yields no
/// chunks — the caller's voice flow short-circuits to a silent reply.
/// </summary>
/// <remarks>
/// Concurrency: ONNX <c>InferenceSession.Run()</c> is not safe under
/// concurrent calls per session, so each <see cref="F5LanguageEngine"/>
/// owns its own gate (<see cref="F5LanguageEngine.SyncRoot"/>). Distinct
/// languages can synthesise in parallel.
/// </remarks>
public sealed class VoxMindTtsProvider : ITtsProvider, IDisposable
{
    private readonly VoxMindOptions _options;
    private readonly ILogger<VoxMindTtsProvider> _logger;
    private readonly ILogger<F5LanguageEngine> _engineLogger;
    private readonly LruEngineCache<F5LanguageEngine> _cache;
    private readonly VoxMindMetrics _metrics;
    private bool _disposed;

    public VoxMindTtsProvider(
        IOptions<VoxMindOptions> options,
        ILogger<VoxMindTtsProvider> logger,
        ILogger<F5LanguageEngine> engineLogger,
        VoxMindMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metrics);
        _options = options.Value;
        _logger = logger;
        _engineLogger = engineLogger;
        _metrics = metrics;
        _cache = new LruEngineCache<F5LanguageEngine>(Math.Max(1, _options.Tts.CacheCapacity));

        var available = _options.Tts.Languages
            .Where(kv => CheckpointExists(kv.Value))
            .Select(kv => kv.Key)
            .ToArray();

        if (available.Length == 0)
        {
            _logger.LogInformation(
                "VoxMind TTS: no F5-TTS checkpoint available on disk ({N} declared) — synthesis disabled.",
                _options.Tts.Languages.Count);
        }
        else
        {
            _logger.LogInformation(
                "VoxMind TTS: {N} language(s) available on demand: {Langs}.",
                available.Length, string.Join(", ", available));
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TtsChunk> SynthesizeAsync(
        string text,
        string? voice = null,
        string? language = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        _ = voice; // reserved for voice-prompt selection (future work)

        var lang = ResolveLanguage(language);
        if (!_options.Tts.Languages.TryGetValue(lang, out var checkpoint))
        {
            _logger.LogDebug(
                "VoxMind TTS: no checkpoint configured for '{Lang}' — skipping synthesis.", lang);
            yield break;
        }

        if (!CheckpointExists(checkpoint))
        {
            _logger.LogDebug(
                "VoxMind TTS: checkpoint files for '{Lang}' missing on disk — skipping synthesis.", lang);
            yield break;
        }

        byte[] wav;
        try
        {
            wav = await SynthesizeWavAsync(text, lang, checkpoint, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            yield break;
        }

        yield return new TtsChunk(Audio: wav, Format: "wav", Visemes: null);
    }

    /// <inheritdoc />
    public Task WarmUpAsync(string? language, CancellationToken ct = default)
    {
        var lang = ResolveLanguage(language);
        if (!_options.Tts.Languages.TryGetValue(lang, out var checkpoint) || !CheckpointExists(checkpoint))
        {
            return Task.CompletedTask;
        }

        // Cold-load (~2-4 s for the 3 ONNX sessions) is offloaded to the thread
        // pool — the caller (typically SubmitVoiceInputHandler) fires this in
        // parallel with the upstream LLM stream so the latency is masked.
        return Task.Run(() => GetOrLoadEngine(lang, checkpoint), ct);
    }

    private async Task<byte[]> SynthesizeWavAsync(
        string text, string lang, F5LanguageCheckpoint checkpoint, CancellationToken ct)
    {
        _metrics.TtsRequests.Add(1, new KeyValuePair<string, object?>("language", lang));
        var sw = Stopwatch.StartNew();
        try
        {
            var engine = GetOrLoadEngine(lang, checkpoint);
            return await SynthesizeOnEngineAsync(engine, text, lang, ct).ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            _metrics.TtsDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("language", lang));
        }
    }

    private F5LanguageEngine GetOrLoadEngine(string lang, F5LanguageCheckpoint checkpoint)
    {
        var hit = _cache.TryGet(lang, out _);
        if (hit)
        {
            _metrics.TtsCacheHits.Add(1, new KeyValuePair<string, object?>("language", lang));
        }
        else
        {
            _metrics.TtsCacheMisses.Add(1, new KeyValuePair<string, object?>("language", lang));
        }
        return _cache.GetOrLoad(lang, () => F5LanguageEngine.LoadFromDisk(checkpoint, _engineLogger));
    }

    private async Task<byte[]> SynthesizeOnEngineAsync(
        F5LanguageEngine engine, string text, string lang, CancellationToken ct)
    {
        await engine.SyncRoot.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(engine.DefaultReferenceWav))
            {
                throw new FileNotFoundException(
                    $"VoxMind TTS: default reference voice not found for '{lang}': {engine.DefaultReferenceWav}.",
                    engine.DefaultReferenceWav);
            }

            var rawRef = await File.ReadAllBytesAsync(engine.DefaultReferenceWav, ct).ConfigureAwait(false);
            var (refMono, refRate) = WavReader.ReadPcm16(rawRef);
            var refPcm = WavReader.Resample(refMono, refRate, F5TtsDecoder.SampleRate);
            var refText = engine.DefaultReferenceText;

            var promptIds = engine.Tokenizer.Encode(refText);
            var targetIds = engine.Tokenizer.Encode(text);
            var steps = _options.Tts.FlowMatchingSteps;

            var pcm = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var conditioning = engine.Preprocessor.Run(refPcm, promptIds, targetIds);
                var mel = engine.Transformer.Sample(conditioning, steps, ct);
                return engine.Decoder.Decode(mel);
            }, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "VoxMind TTS [{Lang}]: {Chars} chars → {Samples} samples ({Duration:F2}s).",
                lang, text.Length, pcm.Length, pcm.Length / (double)F5TtsDecoder.SampleRate);

            return WavWriter.ToBytes(pcm, F5TtsDecoder.SampleRate);
        }
        finally
        {
            engine.SyncRoot.Release();
        }
    }

    private string ResolveLanguage(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) && _options.Tts.Languages.ContainsKey(requested))
        {
            return requested;
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            _logger.LogDebug(
                "VoxMind TTS: language '{Req}' not configured, falling back to '{Default}'.",
                requested, _options.DefaultLanguage);
        }
        return _options.DefaultLanguage;
    }

    private static bool CheckpointExists(F5LanguageCheckpoint c)
        => !string.IsNullOrWhiteSpace(c.PreprocessModelPath)
        && File.Exists(c.PreprocessModelPath)
        && File.Exists(c.TransformerModelPath)
        && File.Exists(c.DecodeModelPath)
        && File.Exists(c.TokensPath);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // LruEngineCache.Dispose iterates each resident F5LanguageEngine
        // and disposes it; F5LanguageEngine.Dispose drains its SyncRoot
        // before releasing native ONNX sessions, giving us graceful shutdown
        // even if a synthesis is in flight.
        _cache.Dispose();
    }
}
