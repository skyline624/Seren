using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:chat:history:begin</c> event emitted by the
/// server immediately before the first <c>output:chat:history:item</c>
/// of an initial hydration burst (not emitted on scroll-back). Signals
/// to the client that an authoritative batch is about to arrive and
/// lets it discard any currently-displayed messages so the hydration
/// replaces rather than merges with stale local state (which might
/// still carry client-generated ids from previous live streams).
/// </summary>
[ExportTsClass]
public sealed record ChatHistoryBeginPayload
{
    /// <summary>
    /// When true, the client should drop any currently-displayed messages
    /// and treat the upcoming batch as the single source of truth.
    /// </summary>
    public bool Reset { get; init; } = true;
}
