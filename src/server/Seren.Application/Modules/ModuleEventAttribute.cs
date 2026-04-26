namespace Seren.Application.Modules;

/// <summary>
/// Tags a constant or property that declares a module-scoped WebSocket
/// event type. Purely informational — used by tooling (TypeGen, doc
/// extraction) to group event constants by their owning module without
/// touching the canonical core <c>EventTypes</c> catalog.
/// </summary>
/// <remarks>
/// Modules add their own event-type constants in their assembly and tag
/// them with this attribute; the core <c>Seren.Contracts.Events.EventTypes</c>
/// stays small and unchanged (additive convention).
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class ModuleEventAttribute : Attribute
{
    public ModuleEventAttribute(string moduleId, ModuleEventDirection direction)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            throw new ArgumentException("moduleId is required.", nameof(moduleId));
        }
        ModuleId = moduleId;
        Direction = direction;
    }

    /// <summary>Owning module identifier (kebab-case, matches <see cref="ISerenModule.Id"/>).</summary>
    public string ModuleId { get; }

    /// <summary>Whether the event flows from peer to server, server to peer, or both.</summary>
    public ModuleEventDirection Direction { get; }
}

/// <summary>Direction of a module-declared WebSocket event.</summary>
public enum ModuleEventDirection
{
    /// <summary>Peer → server (handler input).</summary>
    Inbound,

    /// <summary>Server → peer (broadcast output).</summary>
    Outbound,

    /// <summary>Used in both directions (rare, e.g. ack/echo pairs).</summary>
    Both,
}
