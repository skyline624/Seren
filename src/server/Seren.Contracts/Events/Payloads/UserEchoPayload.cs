using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:chat:user</c> event broadcast by the hub to
/// every connected peer except the sender, so multi-tab / multi-device
/// clients observe the user's turn in real time without waiting for
/// OpenClaw's transcript hydration on reload.
/// </summary>
/// <remarks>
/// <see cref="MessageId"/> is the <c>ClientMessageId</c> the sender
/// provided in its <c>input:text</c> payload (server-minted fallback
/// when absent). Receivers that already have a local message with that
/// id — i.e. the originating tab — must treat the echo as a no-op;
/// other peers add it to their store as a fresh bubble.
/// </remarks>
[ExportTsClass]
public sealed record UserEchoPayload
{
    /// <summary>Stable id the sender tagged the message with.</summary>
    public required string MessageId { get; init; }

    /// <summary>Raw user text (no server-side transformation).</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Hub-side timestamp in Unix milliseconds. Used only for local
    /// ordering on receivers; has no role in the server transcript.
    /// </summary>
    public required long TimestampMs { get; init; }

    /// <summary>
    /// Metadata for each attachment the sender joined to the message.
    /// Content bytes are not echoed (the originating tab has the File
    /// locally; peer tabs render a placeholder chip keyed on
    /// <see cref="ChatAttachmentMetadataDto.AttachmentId"/>).
    /// Null or empty = pure text message.
    /// </summary>
    public IReadOnlyList<ChatAttachmentMetadataDto>? Attachments { get; init; }

    /// <summary>
    /// Stable id of the recognised speaker (when this message originated
    /// from a voice utterance the VoxMind speaker subsystem could attribute
    /// to a profile). <c>null</c> for typed messages or when speaker
    /// identification was skipped — the UI then falls back to the generic
    /// <c>You</c> label. Kept as a string on the wire to avoid leaking the
    /// server-side <c>Guid</c> shape into the SDK.
    /// </summary>
    public string? SpeakerId { get; init; }

    /// <summary>
    /// Display label that goes with <see cref="SpeakerId"/> — either the
    /// existing profile name or an auto-assigned <c>Speaker_N</c>. Sent
    /// alongside the id so the receiving tab can render the bubble
    /// without a follow-up lookup.
    /// </summary>
    public string? SpeakerName { get; init; }
}
