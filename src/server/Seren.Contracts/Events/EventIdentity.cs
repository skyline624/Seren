using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events;

/// <summary>
/// Wire representation of an event identifier and its causal parent.
/// </summary>
[ExportTsClass]
public sealed record EventIdentity
{
    /// <summary>Unique identifier of this event (typically a nanoid).</summary>
    public required string Id { get; init; }

    /// <summary>Optional identifier of the event that caused this one (for tracing).</summary>
    public string? ParentId { get; init; }
}
