using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events;

/// <summary>
/// Metadata attached to every Seren WebSocket envelope.
/// </summary>
[ExportTsClass]
public sealed record EventMetadata
{
    /// <summary>Identity of the module that emitted this event.</summary>
    public required ModuleIdentityDto Source { get; init; }

    /// <summary>Event id and optional parent id for causal tracing.</summary>
    public required EventIdentity Event { get; init; }
}
