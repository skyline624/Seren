using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:chat:history:item</c> event — one persisted
/// message from the OpenClaw transcript, sent to a single peer during
/// hydration or scroll-back pagination.
/// </summary>
/// <remarks>
/// The wire shape mirrors a fully-formed message (no streaming chunks).
/// Clients deduplicate against live <c>output:chat:chunk</c> events using
/// <see cref="MessageId"/> when the same message could appear via both
/// paths (rare race when a peer connects mid-stream).
/// </remarks>
[ExportTsClass]
public sealed record ChatHistoryItemPayload
{
    /// <summary>
    /// Stable identifier for this message — derived from the OpenClaw run id
    /// for assistant turns and from a content-hash + ts for user turns.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>"user", "assistant", or "system".</summary>
    public required string Role { get; init; }

    /// <summary>Plain-text content with markers already stripped (no <c>&lt;emotion:*&gt;</c> tags).</summary>
    public required string Content { get; init; }

    /// <summary>Unix epoch milliseconds when the message was originally produced.</summary>
    public long Timestamp { get; init; }

    /// <summary>Optional emotion extracted from the original message markers.</summary>
    public string? Emotion { get; init; }
}
