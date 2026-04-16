using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events;

/// <summary>
/// Wire representation of a module identity. Mirrors <c>Seren.Domain.ValueObjects.ModuleIdentity</c>.
/// </summary>
[ExportTsClass]
public sealed record ModuleIdentityDto
{
    /// <summary>Unique instance identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Plugin type identifier (shared across instances).</summary>
    public required string PluginId { get; init; }

    /// <summary>Optional plugin version.</summary>
    public string? Version { get; init; }

    /// <summary>Optional routing labels (e.g. <c>env=prod</c>).</summary>
    public IReadOnlyDictionary<string, string>? Labels { get; init; }
}
