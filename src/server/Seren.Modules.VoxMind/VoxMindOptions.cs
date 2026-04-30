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

    /// <summary>STT options (Parakeet + Whisper engines).</summary>
    public VoxMindSttOptions Stt { get; set; } = new();

    /// <summary>TTS (F5-TTS) options.</summary>
    public VoxMindTtsOptions Tts { get; set; } = new();

    /// <summary>Speaker recognition (3D-Speaker / sherpa-onnx) options.</summary>
    public VoxMindSpeakerOptions Speakers { get; set; } = new();
}

/// <summary>
/// STT options. Holds per-engine sub-config and the default engine name
/// the router picks when the caller does not provide an explicit hint.
/// </summary>
/// <remarks>
/// Backward-compat: the legacy <see cref="ModelDir"/> field used to point at
/// the Parakeet bundle directly. <c>VoxMindOptionsBackwardCompat</c>
/// (<see cref="IPostConfigureOptions{TOptions}"/>) maps it to
/// <see cref="ParakeetEngineOptions.ModelDir"/> at boot when
/// <see cref="ParakeetEngineOptions.ModelDir"/> is empty, so existing
/// <c>Modules__voxmind__Stt__ModelDir</c> deployments keep working.
/// </remarks>
public sealed class VoxMindSttOptions
{
    /// <summary>
    /// Engine name selected when no per-request override is supplied
    /// (<c>"parakeet"</c> or <c>"whisper"</c>). Kept lower-case so it
    /// matches the wire format from the UI.
    /// </summary>
    public string DefaultEngine { get; set; } = "parakeet";

    /// <summary>
    /// Maximum chunk length (seconds) per inference pass. Parakeet TDT v3
    /// becomes unstable above ~20 s on CPU; default 12 s. Whisper handles
    /// 30 s natively and is uncapped here (its own decoder caps internally).
    /// </summary>
    public double MaxChunkSeconds { get; set; } = 12.0;

    /// <summary>
    /// Legacy single-engine path. Kept for backward-compat: when
    /// <see cref="ParakeetEngineOptions.ModelDir"/> is empty and this is
    /// set, the post-configure adapter copies the value across.
    /// </summary>
    public string ModelDir { get; set; } = string.Empty;

    /// <summary>Parakeet TDT (NVIDIA NeMo) engine config.</summary>
    public ParakeetEngineOptions Parakeet { get; set; } = new();

    /// <summary>Whisper (OpenAI / sherpa-onnx) engine config.</summary>
    public WhisperEngineOptions Whisper { get; set; } = new();
}

/// <summary>
/// Parakeet engine config. Bundle layout:
/// <c>nemo128.onnx</c>, <c>encoder-model.int8.onnx</c>,
/// <c>decoder_joint-model.int8.onnx</c>, <c>vocab.txt</c>.
/// </summary>
public sealed class ParakeetEngineOptions
{
    /// <summary>Directory holding the four ONNX/vocab files. Empty = engine disabled.</summary>
    public string ModelDir { get; set; } = string.Empty;
}

/// <summary>
/// Whisper engine config. Bundle layout (sherpa-onnx Whisper export):
/// <c>{size}-encoder.int8.onnx</c>, <c>{size}-decoder.int8.onnx</c>,
/// <c>{size}-tokens.txt</c>.
/// </summary>
public sealed class WhisperEngineOptions
{
    /// <summary>
    /// Root directory under which downloadable Whisper variants are
    /// installed by the model management endpoints — each variant lives
    /// in <c>{RootDir}/whisper-{size}/</c>. Default
    /// <c>/data/voxmind/models</c> (the Docker volume).
    /// </summary>
    public string RootDir { get; set; } = "/data/voxmind/models";

    /// <summary>
    /// Legacy explicit bundle directory. When set, takes precedence over
    /// the <see cref="RootDir"/>-derived path. Empty = use RootDir layout.
    /// </summary>
    public string ModelDir { get; set; } = string.Empty;

    /// <summary>
    /// Whisper variant used when the caller asks for the legacy
    /// <c>engine=whisper</c> hint without specifying a size. Default
    /// <c>"small"</c> — best quality/latency trade-off for FR on CPU.
    /// </summary>
    public string ModelSize { get; set; } = "small";

    /// <summary>
    /// Optional language hint forwarded to Whisper (<c>"fr"</c>, <c>"en"</c>,
    /// auto-detect when null/empty). Whisper's own language head is reliable
    /// so leaving it blank usually gives correct routing.
    /// </summary>
    public string? Language { get; set; }
}

/// <summary>
/// Speaker recognition options. Hybrid storage: SQLite holds the profile
/// metadata + embedding rows, the actual float vectors live as <c>.bin</c>
/// blobs under <see cref="EmbeddingsDir"/>. Auto-enrol picks
/// <c>{<see cref="AutoEnrolNamePrefix"/>}{N}</c> when no profile matches.
/// </summary>
public sealed class VoxMindSpeakerOptions
{
    /// <summary>
    /// When <c>false</c> the speaker service registers as a no-op —
    /// pipelines still run, every utterance just maps to the generic
    /// <c>You</c> bubble. Default <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Absolute path to the sherpa-onnx embedding model. The default
    /// matches the layout we ship with the <c>voxmind_speakers</c>
    /// docker volume; operators can override per-deployment.
    /// </summary>
    public string ModelPath { get; set; } = "/data/voxmind/speakers/models/3dspeaker_eres2net_base_16k.onnx";

    /// <summary>SQLite database holding profile + embedding metadata.</summary>
    public string DbPath { get; set; } = "/data/voxmind/speakers/speakers.db";

    /// <summary>Directory where <c>.bin</c> embedding blobs are persisted.</summary>
    public string EmbeddingsDir { get; set; } = "/data/voxmind/speakers/embeddings";

    /// <summary>
    /// Cosine similarity threshold above which the best-match profile is
    /// treated as Identified. Below it, a fresh profile is enrolled.
    /// 0.65 mirrors the upstream VoxMind default — a good balance for
    /// 3D-Speaker ERes2Net base on FR/multi voices.
    /// </summary>
    public float ConfidenceThreshold { get; set; } = 0.65f;

    /// <summary>
    /// Audio shorter than this is ignored (NotEnoughAudio). 1.5 s is the
    /// minimum needed for ERes2Net to produce a stable embedding; below
    /// it the result is too noisy to enrol or match.
    /// </summary>
    public double MinAudioDurationSec { get; set; } = 1.5;

    /// <summary>Prefix for auto-enrolled profile names (<c>"Speaker_1"</c>, …).</summary>
    public string AutoEnrolNamePrefix { get; set; } = "Speaker_";

    /// <summary>Number of CPU threads the ONNX runtime is allowed to use during inference.</summary>
    public int NumThreads { get; set; } = 1;
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
