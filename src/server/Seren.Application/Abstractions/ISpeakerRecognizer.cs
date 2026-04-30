namespace Seren.Application.Abstractions;

/// <summary>
/// Application-level abstraction for speaker recognition. Implemented by
/// <c>Seren.Modules.VoxMind</c> via an adapter so the application layer
/// stays decoupled from the sherpa-onnx / EF Core infrastructure.
/// </summary>
/// <remarks>
/// The default no-op registration ships with <c>Seren.Application</c>
/// itself: when the VoxMind module is absent (or disabled), the host
/// resolves <see cref="NoOpSpeakerRecognizer"/> which always returns
/// <see cref="SpeakerRecognitionResult.NotIdentified"/>. The pipeline
/// then transparently falls back to the generic <c>You</c> bubble label.
/// </remarks>
public interface ISpeakerRecognizer
{
    /// <summary>
    /// Attribute the given PCM 16 kHz mono WAV buffer to a speaker
    /// profile. Auto-enrols a fresh profile when no candidate clears the
    /// implementation's confidence threshold; returns
    /// <see cref="SpeakerRecognitionResult.NotIdentified"/> when the
    /// service is dormant or the audio is too short.
    /// </summary>
    Task<SpeakerRecognitionResult> RecognizeAsync(byte[] audioData, CancellationToken ct = default);
}

/// <summary>
/// Outcome of an <see cref="ISpeakerRecognizer.RecognizeAsync"/> call.
/// Carries the resolved profile id + display name when identification
/// succeeded, or empty fields when it didn't.
/// </summary>
/// <param name="SpeakerId">Stable id of the recognised profile (or
/// <c>null</c> when no attribution was made).</param>
/// <param name="SpeakerName">Display label that should appear next to
/// the user's bubble (existing profile name or auto-assigned
/// <c>Speaker_N</c>).</param>
/// <param name="Confidence">Cosine similarity in [0, 1] reported by
/// the underlying engine, mainly for telemetry. Always set, even when
/// no profile was attributed.</param>
public sealed record SpeakerRecognitionResult(
    string? SpeakerId,
    string? SpeakerName,
    float Confidence)
{
    /// <summary>True when both id and name are populated — i.e. the bubble can be tagged.</summary>
    public bool HasSpeaker => !string.IsNullOrEmpty(SpeakerId) && !string.IsNullOrEmpty(SpeakerName);

    /// <summary>Cached singleton used by the no-op recognizer and the failure paths.</summary>
    public static SpeakerRecognitionResult NotIdentified { get; } = new(null, null, 0f);
}

/// <summary>
/// Default <see cref="ISpeakerRecognizer"/> implementation: never tags
/// a speaker. Registered as a fallback so the voice handlers don't need
/// a null check on the dependency.
/// </summary>
public sealed class NoOpSpeakerRecognizer : ISpeakerRecognizer
{
    public Task<SpeakerRecognitionResult> RecognizeAsync(byte[] audioData, CancellationToken ct = default)
        => Task.FromResult(SpeakerRecognitionResult.NotIdentified);
}
