using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>input:chat:history:request</c> event sent by a client UI
/// to request older chat messages — typically when the user scrolls back
/// past the initial hydration window.
/// </summary>
/// <remarks>
/// The hub serves the response on the same WebSocket via a sequence of
/// <c>output:chat:history:item</c> events terminated by
/// <c>output:chat:history:end</c>. Only the requesting peer receives the
/// response — other connected peers are not affected.
/// </remarks>
[ExportTsClass]
public sealed record ChatHistoryRequestPayload
{
    /// <summary>
    /// Maximum number of messages to return. Defaults applied server-side
    /// when omitted (typically 30 for scroll-back, 50 for first hydration).
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Cursor: only return messages whose <c>messageId</c> is older than
    /// this value. Use the oldest <c>messageId</c> currently displayed in
    /// the UI for scroll-back pagination.
    /// </summary>
    public string? Before { get; init; }
}
