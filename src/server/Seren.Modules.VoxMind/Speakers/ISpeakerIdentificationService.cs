namespace Seren.Modules.VoxMind.Speakers;

/// <summary>
/// Identifies the speaker behind a piece of audio (or skips gracefully
/// when the model is missing / the clip is too short). v1 scope is the
/// single-speaker-per-utterance path used by Seren's voice handlers.
/// Diarisation, verbal-correction NLU, and profile management UI are
/// out of scope and tracked in the project plan as v2/v3 chantiers.
/// </summary>
/// <remarks>
/// Lifetime: Singleton. The implementation owns the sherpa-onnx
/// extractor + an in-memory embedding cache rebuilt at boot via
/// <see cref="InitializeAsync"/>; a single instance is the only safe
/// resident given the native handle ownership.
/// </remarks>
public interface ISpeakerIdentificationService
{
    /// <summary>
    /// <c>true</c> when the sherpa-onnx extractor is loaded and ready to
    /// answer identification calls. Health checks read this without
    /// touching the model files.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Lazily warm the in-memory embedding cache from SQLite. Idempotent —
    /// the second call is a no-op once the cache is populated. The boot
    /// hook in <c>VoxMindModule</c> kicks this off in the background so
    /// the first identification call doesn't pay the cache load cost.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Identify the speaker behind a PCM 16 kHz mono WAV buffer. When no
    /// profile matches above the configured threshold, an automatic
    /// profile <c>Speaker_N</c> is enrolled and returned. Audio shorter
    /// than the configured minimum yields
    /// <see cref="SpeakerIdentificationOutcome.NotEnoughAudio"/>.
    /// </summary>
    Task<SpeakerIdentificationResult> IdentifyFromAudioAsync(
        byte[] audioData, CancellationToken ct = default);

    /// <summary>List active profiles (read-only domain projection).</summary>
    Task<IReadOnlyList<SpeakerProfile>> GetAllProfilesAsync(CancellationToken ct = default);
}
