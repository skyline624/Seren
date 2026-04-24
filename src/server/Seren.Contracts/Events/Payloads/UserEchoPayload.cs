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
}
