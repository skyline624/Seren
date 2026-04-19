using System.Text.Json;
using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:agent:event</c> event broadcast by the hub when
/// OpenClaw surfaces an agent lifecycle or tool-call event (e.g. a tool
/// starting, finishing, or producing partial output). Useful for UI
/// affordances such as "the assistant is calling tool X".
/// </summary>
[ExportTsClass]
public sealed record AgentEventPayload
{
    /// <summary>OpenClaw run identifier this event belongs to.</summary>
    public required string RunId { get; init; }

    /// <summary>Session the run is tied to (same key as the originating chat.send).</summary>
    public string? SessionKey { get; init; }

    /// <summary>Event stream bucket upstream (e.g. "tool", "item").</summary>
    public required string Stream { get; init; }

    /// <summary>Lifecycle phase ("start", "delta", "end", "error" upstream, or free-form).</summary>
    public string? Phase { get; init; }

    /// <summary>Monotonic sequence within the run.</summary>
    public long? Seq { get; init; }

    /// <summary>
    /// Raw upstream <c>data</c> field forwarded as-is. Kept as
    /// <see cref="JsonElement"/> because the payload shape varies by agent
    /// implementation and we prefer to expose it verbatim to the UI rather
    /// than re-model every variant server-side.
    /// </summary>
    public JsonElement? Data { get; init; }
}
