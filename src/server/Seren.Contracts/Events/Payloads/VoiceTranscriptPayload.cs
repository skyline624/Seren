using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:voice:transcript</c> event — STT result returned
/// to the originating peer of an <c>input:voice:transcribe</c> request.
/// Sent unicast (not broadcast) because this is a transient transcription
/// the user has not yet committed to chat.
/// </summary>
[ExportTsClass]
public sealed record VoiceTranscriptPayload
{
    /// <summary>
    /// Echo of <see cref="VoiceTranscribePayload.RequestId"/> so the UI can
    /// resolve the matching in-flight promise.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>The transcribed text. May be empty if the audio was silent or unintelligible.</summary>
    public required string Text { get; init; }

    /// <summary>Detected language (ISO 639-1) when the STT engine reports one.</summary>
    public string? Language { get; init; }

    /// <summary>Confidence score in [0, 1] when the engine surfaces it.</summary>
    public float? Confidence { get; init; }

    /// <summary>
    /// Stable machine-readable code from <c>SttErrorCodes</c> when the
    /// STT engine could not produce a usable transcript. <c>null</c> on
    /// success or genuine silence (handled inline by the empty
    /// <see cref="Text"/>). The dictate UI rejects the in-flight promise
    /// with the localized message keyed by this code.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Optional human-readable detail safe to display alongside the
    /// localized headline.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Stable speaker profile id when the VoxMind subsystem could
    /// attribute the dictation audio to a voice profile (existing or
    /// freshly auto-enrolled). <c>null</c> when speaker recognition is
    /// dormant or the clip was below the minimum duration.
    /// </summary>
    public string? SpeakerId { get; init; }

    /// <summary>Display label for <see cref="SpeakerId"/> (e.g. <c>Speaker_3</c>).</summary>
    public string? SpeakerName { get; init; }
}
