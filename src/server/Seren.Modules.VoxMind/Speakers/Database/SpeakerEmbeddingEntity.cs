namespace Seren.Modules.VoxMind.Speakers.Database;

/// <summary>
/// Persisted shape of one captured embedding. The actual float[] vector
/// is not stored in SQLite — it lives at <see cref="FilePath"/> as a raw
/// little-endian binary blob (size = vector_dim * 4 bytes). SQLite owns
/// only metadata + the path to read on identification.
/// </summary>
/// <remarks>
/// Hybrid storage rationale (port from upstream VoxMind): keeps SQLite
/// rows tiny so the in-RAM cache build is fast (one BLOB read per
/// embedding), and lets the operator inspect / back up the embeddings as
/// regular files without dumping the database.
/// </remarks>
public sealed class SpeakerEmbeddingEntity
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    /// <summary>Absolute path to the <c>.bin</c> blob holding the float vector.</summary>
    public string FilePath { get; set; } = string.Empty;

    public DateTime CapturedAt { get; set; }

    public float InitialConfidence { get; set; }

    public int AudioDurationSeconds { get; set; }

    /// <summary>Navigation back to the parent profile (FK <c>ProfileId</c>).</summary>
    public SpeakerProfileEntity Profile { get; set; } = null!;
}
