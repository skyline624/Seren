using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:voice:error</c> event — sent by the server
/// to the originating peer when the chat-mic pipeline could not
/// produce a transcript. Carries enough context for the UI to resolve
/// its optimistic placeholder bubble: localized error rendering for
/// real failures, silent removal for genuine silence.
/// </summary>
/// <remarks>
/// The dictate flow doesn't use this event — it surfaces equivalent
/// state inline via <see cref="VoiceTranscriptPayload.ErrorCode"/> +
/// <see cref="VoiceTranscriptPayload.ErrorMessage"/> on the existing
/// <c>output:voice:transcript</c> reply, since the client is already
/// awaiting that single response.
/// </remarks>
[ExportTsClass]
public sealed record VoiceErrorPayload
{
    /// <summary>
    /// Echo of <see cref="VoiceInputPayload.ClientMessageId"/> so the
    /// UI can locate and resolve the corresponding placeholder bubble.
    /// </summary>
    public string? ClientMessageId { get; init; }

    /// <summary>
    /// Stable machine-readable code from <c>SttErrorCodes</c>:
    /// <c>"engine_unavailable"</c>, <c>"engine_failed"</c>,
    /// <c>"audio_decode_failed"</c>, or <c>"silent"</c>. The UI maps
    /// this to a localized message; <c>"silent"</c> means "remove the
    /// placeholder without raising an error".
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Optional human-readable detail safe to display (engine variant
    /// name, native lib hint, etc.). The UI uses it as a tooltip /
    /// secondary line under the localized headline.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Engine name as resolved by the router (e.g. <c>"whisper"</c>
    /// or <c>"parakeet"</c>) — useful for diagnostics; may be
    /// <c>null</c> when no engine could be selected at all.
    /// </summary>
    public string? Engine { get; init; }
}
