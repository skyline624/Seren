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
}
