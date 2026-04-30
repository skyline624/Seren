namespace Seren.Modules.VoxMind.Speakers;

/// <summary>
/// Outcome of an identification attempt. Lets the pipeline distinguish a
/// match against a known profile (Identified), the auto-creation of a new
/// profile (Enrolled), a graceful skip when the audio is too short or the
/// engine is unavailable (NotEnoughAudio / Unavailable), and a hard
/// failure (Failed). No outcome maps to a silent drop — the caller always
/// gets a typed result.
/// </summary>
public enum SpeakerIdentificationOutcome
{
    /// <summary>Best-match cosine similarity ≥ confidence threshold; <see cref="SpeakerIdentificationResult.ProfileId"/> + <see cref="SpeakerIdentificationResult.SpeakerName"/> set.</summary>
    Identified,

    /// <summary>No profile matched; a fresh <c>Speaker_N</c> was created and persisted.</summary>
    Enrolled,

    /// <summary>Audio was below <c>MinAudioDurationSec</c> or yielded no embedding — skipped on purpose.</summary>
    NotEnoughAudio,

    /// <summary>The sherpa-onnx extractor / model isn't on disk; service is dormant for this request.</summary>
    Unavailable,

    /// <summary>Embedding extraction or DB access threw; details in the log, no profile mutation.</summary>
    Failed,
}

/// <summary>
/// Typed result of <see cref="ISpeakerIdentificationService.IdentifyFromAudioAsync"/>.
/// </summary>
/// <param name="Outcome">High-level classification used by the pipeline branch.</param>
/// <param name="ProfileId">Resolved or freshly enrolled profile id; <c>null</c> when no profile was created.</param>
/// <param name="SpeakerName">Display name (existing label or auto-assigned <c>Speaker_N</c>); <c>null</c> when no name applies.</param>
/// <param name="Confidence">Best cosine similarity observed (0-1). Always set, even when <c>Outcome != Identified</c>, for telemetry.</param>
public sealed record SpeakerIdentificationResult(
    SpeakerIdentificationOutcome Outcome,
    Guid? ProfileId,
    string? SpeakerName,
    float Confidence)
{
    /// <summary>Convenience flag — <c>true</c> when the result carries a usable speaker label.</summary>
    public bool HasSpeaker => ProfileId.HasValue && !string.IsNullOrWhiteSpace(SpeakerName);

    /// <summary>Generic short-circuit result when the engine is dormant (model missing).</summary>
    public static SpeakerIdentificationResult NotAvailable { get; } =
        new(SpeakerIdentificationOutcome.Unavailable, null, null, 0f);

    /// <summary>Used when the audio is shorter than the configured minimum.</summary>
    public static SpeakerIdentificationResult AudioTooShort { get; } =
        new(SpeakerIdentificationOutcome.NotEnoughAudio, null, null, 0f);
}
