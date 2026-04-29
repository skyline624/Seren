namespace Seren.Application.Abstractions;

/// <summary>
/// Result of a speech-to-text transcription. When the engine succeeds,
/// <see cref="Text"/> carries the transcript and both error fields are
/// <c>null</c>. When the engine could not produce a transcript (native
/// lib missing, model bundle corrupted, audio decode failure, mid-decode
/// crash) the handler surfaces <see cref="ErrorCode"/> +
/// <see cref="ErrorMessage"/> to the client instead of silently dropping
/// the request — there is no auto-fallback to another engine, the user
/// sees clearly what went wrong.
/// </summary>
/// <param name="Text">The transcribed text. Empty string for genuine
/// silence (no <see cref="ErrorCode"/>) or for any error path.</param>
/// <param name="Language">Detected language code (e.g. "en", "ja"), or
/// <c>null</c> if not detected.</param>
/// <param name="Confidence">Confidence score between 0 and 1, or
/// <c>null</c> if unavailable.</param>
/// <param name="ErrorCode">Stable machine-readable code from
/// <see cref="SttErrorCodes"/>; <c>null</c> on success / silence.</param>
/// <param name="ErrorMessage">Optional human-readable detail safe to
/// surface to the user (e.g. the failing variant name + reason).</param>
public sealed record SttResult(
    string Text,
    string? Language = null,
    float? Confidence = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

/// <summary>
/// Stable machine-readable codes for STT failures. Mirrored on the wire
/// (<c>VoiceErrorPayload.Code</c>, <c>VoiceTranscriptPayload.ErrorCode</c>)
/// so the UI can render localized messages without parsing free-form
/// strings.
/// </summary>
public static class SttErrorCodes
{
    /// <summary>
    /// Selected engine cannot run on this host — typically a missing
    /// native dependency or a corrupted/incomplete model bundle. The
    /// user must download / re-deploy the bundle (or pick another
    /// engine) before retrying.
    /// </summary>
    public const string EngineUnavailable = "engine_unavailable";

    /// <summary>
    /// Engine loaded fine but inference threw mid-decode (buffer
    /// shape mismatch, ONNX runtime error, OOM, …). Retrying may help;
    /// repeated failures indicate a deeper issue.
    /// </summary>
    public const string EngineFailed = "engine_failed";

    /// <summary>
    /// Input audio could not be decoded into PCM (FFmpeg failure,
    /// malformed container, unsupported codec). Distinct from engine
    /// errors so the UI can hint at re-recording.
    /// </summary>
    public const string AudioDecodeFailed = "audio_decode_failed";

    /// <summary>
    /// Pipeline succeeded but the transcript is empty (the user did
    /// not speak / VAD captured silence). Not an error per se; the UI
    /// uses it to discard the optimistic placeholder bubble silently.
    /// </summary>
    public const string Silent = "silent";
}
