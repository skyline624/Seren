using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of a <c>module:announce</c> event sent by a client to the hub.
/// </summary>
[ExportTsClass]
public sealed record AnnouncePayload
{
    /// <summary>Identity of the announcing module.</summary>
    public required ModuleIdentityDto Identity { get; init; }

    /// <summary>Human-readable module name.</summary>
    public required string Name { get; init; }

    /// <summary>Event types this module is able to emit or consume.</summary>
    public IReadOnlyList<string>? PossibleEvents { get; init; }
}

/// <summary>
/// Payload of a <c>module:announced</c> event broadcast by the hub to confirm
/// a successful announce.
/// </summary>
[ExportTsClass]
public sealed record AnnouncedPayload
{
    /// <summary>Identity of the announced module.</summary>
    public required ModuleIdentityDto Identity { get; init; }

    /// <summary>Human-readable module name.</summary>
    public required string Name { get; init; }

    /// <summary>Instance index assigned by the hub (for multi-instance modules).</summary>
    public int Index { get; init; }
}
