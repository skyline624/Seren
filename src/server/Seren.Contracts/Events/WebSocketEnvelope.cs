using System.Text.Json;
using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events;

/// <summary>
/// The root wire type exchanged between Seren clients (UI, plugins) and the hub.
/// </summary>
/// <remarks>
/// <para>
/// On the wire, all envelopes are JSON objects with the shape:
/// </para>
/// <code>
/// {
///   "type": "input:text",
///   "data": { ... },
///   "metadata": {
///     "source": { "id": "...", "pluginId": "..." },
///     "event":  { "id": "...", "parentId": "..." }
///   }
/// }
/// </code>
/// <para>
/// The <see cref="Data"/> payload is kept as a <see cref="JsonElement"/> so that the
/// hub dispatcher can inspect <see cref="Type"/> first and then deserialize the payload
/// into the concrete type handled by the matching Mediator command.
/// </para>
/// </remarks>
[ExportTsClass]
public sealed record WebSocketEnvelope
{
    /// <summary>The event type name — see <see cref="EventTypes"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Typed payload, kept as raw JSON until dispatched.</summary>
    [TsType("any")]
    public JsonElement Data { get; init; }

    /// <summary>Source identity and event identifier.</summary>
    public required EventMetadata Metadata { get; init; }
}
