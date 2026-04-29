using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Modules.VoxMind.Diagnostics;

namespace Seren.Modules.VoxMind.Transcription;

/// <summary>
/// Public-facing <see cref="ISttProvider"/> for the VoxMind module. Acts as
/// a router that dispatches a transcription request to one of the local
/// engines (Parakeet, Whisper) based on the per-request hint or the
/// configured default. Unknown hints fall back to the default.
/// </summary>
/// <remarks>
/// Engines are injected as a collection (<see cref="IEnumerable{T}"/>) so
/// adding a new engine is a one-line DI registration in
/// <c>VoxMindModule.Configure</c>. The router itself owns no inference
/// resources — each engine handles its own ONNX session lifecycle and
/// concurrency gate.
/// </remarks>
public sealed class VoxMindSttProvider : ISttProvider
{
    private readonly VoxMindOptions _options;
    private readonly ILogger<VoxMindSttProvider> _logger;
    private readonly VoxMindMetrics _metrics;
    private readonly IReadOnlyDictionary<string, IVoxMindSttEngine> _engines;

    public VoxMindSttProvider(
        IOptions<VoxMindOptions> options,
        ILogger<VoxMindSttProvider> logger,
        VoxMindMetrics metrics,
        IEnumerable<IVoxMindSttEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(engines);

        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
        _engines = engines
            .ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .AsReadOnly();

        if (_engines.Count == 0)
        {
            _logger.LogWarning("VoxMind STT: no engine registered — provider will return empty results.");
        }
        else
        {
            _logger.LogInformation(
                "VoxMind STT: router ready with {Count} engine(s): {Engines}. Default: {Default}.",
                _engines.Count, string.Join(", ", _engines.Keys), _options.Stt.DefaultEngine);
        }
    }

    /// <summary>Names of the engines registered in this router (test probe).</summary>
    internal IReadOnlyCollection<string> RegisteredEngineNames => _engines.Keys.ToArray();

    /// <inheritdoc />
    public Task<SttResult> TranscribeAsync(byte[] audioData, string format, CancellationToken ct = default)
        => TranscribeAsync(audioData, format, engineHint: null, languageHint: null, ct);

    /// <inheritdoc />
    public Task<SttResult> TranscribeAsync(
        byte[] audioData, string format, string? engineHint, CancellationToken ct = default)
        => TranscribeAsync(audioData, format, engineHint, languageHint: null, ct);

    /// <inheritdoc />
    public async Task<SttResult> TranscribeAsync(
        byte[] audioData,
        string format,
        string? engineHint,
        string? languageHint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audioData);
        ArgumentNullException.ThrowIfNull(format);

        // Variant-aware: when the hint includes a size suffix
        // ("whisper-tiny", "whisper-large", …) extract the bare size
        // before resolving the engine — the router maps the family name
        // to a concrete engine and forwards the size as a separate
        // parameter when the engine supports per-variant cache.
        var (familyHint, variantHint) = ParseHint(engineHint);
        var normalisedLanguage = NormaliseLanguage(languageHint);

        var engine = SelectEngine(familyHint);
        if (engine is null)
        {
            _logger.LogWarning(
                "VoxMind STT: no engine available (requested='{Hint}', default='{Default}'). "
                + "Returning empty result.",
                engineHint, _options.Stt.DefaultEngine);
            return new SttResult(string.Empty, _options.DefaultLanguage, Confidence: 0f);
        }

        _metrics.SttRequests.Add(1,
            new KeyValuePair<string, object?>("engine", engine.Name),
            new KeyValuePair<string, object?>("variant", variantHint ?? "default"),
            new KeyValuePair<string, object?>("language", normalisedLanguage ?? "auto"));
        var sw = Stopwatch.StartNew();
        try
        {
            if (engine is IVoxMindVariantAwareEngine variantEngine)
            {
                // Resolve the variant: explicit hint > engine's configured default.
                // For Whisper that's `Stt.Whisper.ModelSize`; future variant-aware
                // engines provide their own fallback inside `TranscribeAsync`.
                var size = !string.IsNullOrWhiteSpace(variantHint)
                    ? variantHint
                    : _options.Stt.Whisper.ModelSize;
                return await variantEngine
                    .TranscribeAsync(audioData, format, size, normalisedLanguage, ct)
                    .ConfigureAwait(false);
            }

            return await engine.TranscribeAsync(audioData, format, ct).ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            _metrics.SttDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("engine", engine.Name),
                new KeyValuePair<string, object?>("variant", variantHint ?? "default"),
                new KeyValuePair<string, object?>("language", normalisedLanguage ?? "auto"));
        }
    }

    /// <summary>
    /// Normalises a UI-provided language hint into the wire form the
    /// engine expects. Whitespace, <c>null</c>, and <c>"auto"</c> all
    /// collapse to <c>null</c> (= "let the engine pick").
    /// </summary>
    private static string? NormaliseLanguage(string? languageHint)
    {
        if (string.IsNullOrWhiteSpace(languageHint))
        {
            return null;
        }

        var trimmed = languageHint.Trim();
        return string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Splits a wire-format engine hint into its (family, variant) pair.
    /// <c>"whisper-tiny"</c> → <c>("whisper", "tiny")</c>, <c>"parakeet"</c>
    /// → <c>("parakeet", null)</c>, <c>null</c> → <c>(null, null)</c>.
    /// </summary>
    private static (string? Family, string? Variant) ParseHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return (null, null);
        }

        var dash = hint.IndexOf('-', StringComparison.Ordinal);
        if (dash <= 0 || dash == hint.Length - 1)
        {
            return (hint, null);
        }

        return (hint[..dash], hint[(dash + 1)..]);
    }

    /// <summary>
    /// Resolution order: per-request hint → configured default → first
    /// available engine. Unknown hints log a warning and fall through to
    /// the default. Disabled engines (no model on disk) are skipped.
    /// </summary>
    private IVoxMindSttEngine? SelectEngine(string? engineHint)
    {
        if (!string.IsNullOrWhiteSpace(engineHint))
        {
            if (_engines.TryGetValue(engineHint, out var requested))
            {
                if (requested.IsAvailable)
                {
                    return requested;
                }
                _logger.LogWarning(
                    "VoxMind STT: engine '{Engine}' was requested but is not available "
                    + "(model bundle missing on disk). Falling back to default.", engineHint);
            }
            else
            {
                _logger.LogWarning(
                    "VoxMind STT: unknown engine '{Engine}' requested. Known: {Known}. "
                    + "Falling back to default.", engineHint, string.Join(", ", _engines.Keys));
            }
        }

        var defaultName = _options.Stt.DefaultEngine;
        if (!string.IsNullOrWhiteSpace(defaultName)
            && _engines.TryGetValue(defaultName, out var fallback)
            && fallback.IsAvailable)
        {
            return fallback;
        }

        // Last resort: the first available engine, regardless of name. Keeps
        // the system functional in mixed-deployment scenarios where only
        // one bundle has been pushed to the volume.
        return _engines.Values.FirstOrDefault(e => e.IsAvailable);
    }
}
