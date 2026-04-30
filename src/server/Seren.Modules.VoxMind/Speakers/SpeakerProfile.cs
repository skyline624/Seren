namespace Seren.Modules.VoxMind.Speakers;

/// <summary>
/// Domain projection of a speaker profile. Distinct from
/// <see cref="Database.SpeakerProfileEntity"/> on purpose: keeps the
/// service contract free of EF Core navigation properties and lets us
/// evolve the persistence schema without leaking changes to consumers.
/// </summary>
public sealed class SpeakerProfile
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    public DateTime CreatedAt { get; init; }

    public DateTime? LastSeenAt { get; init; }

    public int DetectionCount { get; init; }

    public bool IsActive { get; init; } = true;

    public string? Notes { get; init; }
}
