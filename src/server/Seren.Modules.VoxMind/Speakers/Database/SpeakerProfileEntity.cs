namespace Seren.Modules.VoxMind.Speakers.Database;

/// <summary>
/// Persisted shape of a speaker profile. Mirrors the upstream VoxMind
/// schema (port verbatim) so the SQLite layout — and therefore the EF
/// migration — stays identical to the source-of-truth project. The
/// runtime <see cref="SpeakerProfile"/> domain record is a separate
/// type to keep persistence concerns out of the application core.
/// </summary>
public sealed class SpeakerProfileEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>JSON array of additional names linked to this profile (verbal aliases).</summary>
    public string? AliasesJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public int DetectionCount { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    /// <summary>Embeddings collected for this speaker (one row per <c>.bin</c> file).</summary>
    public List<SpeakerEmbeddingEntity> Embeddings { get; set; } = new();
}
