using Seren.Modules.VoxMind.F5Tts;

namespace Seren.Modules.VoxMind;

/// <summary>
/// Configuration options for the VoxMind voice module.
/// Bound from <c>Modules:VoxMind</c>.
/// </summary>
public sealed class VoxMindOptions
{
    /// <summary>Default configuration section name (legacy lookup).</summary>
    public const string SectionName = "VoxMind";

    /// <summary>
    /// When <c>false</c> the module skips real provider registration and falls
    /// back to no-op transcription/synthesis.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default ISO 639-1 code used when neither the caller nor the upstream
    /// language detector provides one.
    /// </summary>
    public string DefaultLanguage { get; set; } = "fr";

    /// <summary>STT (Parakeet) options.</summary>
    public VoxMindSttOptions Stt { get; set; } = new();

    /// <summary>TTS (F5-TTS) options.</summary>
    public VoxMindTtsOptions Tts { get; set; } = new();
}

/// <summary>
/// Parakeet STT options. Keep the ONNX bundle outside the repo (huge weights);
/// point <see cref="ModelDir"/> at a directory containing the four expected
/// files: <c>nemo128.onnx</c>, <c>encoder-model.int8.onnx</c>,
/// <c>decoder_joint-model.int8.onnx</c>, <c>vocab.txt</c>.
/// </summary>
public sealed class VoxMindSttOptions
{
    /// <summary>Directory holding the Parakeet ONNX bundle. Empty = STT disabled.</summary>
    public string ModelDir { get; set; } = string.Empty;

    /// <summary>
    /// Maximum chunk length (seconds) the Parakeet encoder consumes in one
    /// inference pass. Above this we split the audio. Parakeet TDT v3 is
    /// safe up to ~20 s; we default to a conservative 12 s.
    /// </summary>
    public double MaxChunkSeconds { get; set; } = 12.0;
}

/// <summary>
/// F5-TTS options including the per-language checkpoint table and the LRU
/// engine cache capacity.
/// </summary>
public sealed class VoxMindTtsOptions
{
    /// <summary>
    /// Number of Euler integration steps for the flow-matching transformer.
    /// Higher = better quality, slower. The DakeQQ port recommends 32 for
    /// production, 16 for fast previews.
    /// </summary>
    public int FlowMatchingSteps { get; set; } = 32;

    /// <summary>
    /// Maximum number of resident F5 engines (one per language). Each engine
    /// occupies ~1.5 GB of RAM; default 2 keeps FR + EN hot simultaneously.
    /// </summary>
    public int CacheCapacity { get; set; } = 2;

    /// <summary>Per-language checkpoint table, keyed by ISO 639-1 code.</summary>
    public Dictionary<string, F5LanguageCheckpoint> Languages { get; set; } = new(StringComparer.Ordinal);
}
