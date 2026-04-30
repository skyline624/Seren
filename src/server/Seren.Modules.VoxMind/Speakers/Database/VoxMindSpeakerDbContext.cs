using Microsoft.EntityFrameworkCore;

namespace Seren.Modules.VoxMind.Speakers.Database;

/// <summary>
/// EF Core context for the VoxMind speaker subsystem. Owns the speaker
/// profiles + their captured embeddings. Wired as a
/// <see cref="IDbContextFactory{TContext}"/> so the singleton speaker
/// service can spin short-lived contexts on demand without registering a
/// scoped lifetime in the root container.
/// </summary>
public sealed class VoxMindSpeakerDbContext : DbContext
{
    public DbSet<SpeakerProfileEntity> SpeakerProfiles => Set<SpeakerProfileEntity>();
    public DbSet<SpeakerEmbeddingEntity> SpeakerEmbeddings => Set<SpeakerEmbeddingEntity>();

    public VoxMindSpeakerDbContext(DbContextOptions<VoxMindSpeakerDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SpeakerProfileEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired();
            e.HasIndex(p => p.LastSeenAt).HasDatabaseName("idx_profiles_lastseen");
        });

        modelBuilder.Entity<SpeakerEmbeddingEntity>(e =>
        {
            e.HasKey(em => em.Id);
            e.HasOne(em => em.Profile)
             .WithMany(p => p.Embeddings)
             .HasForeignKey(em => em.ProfileId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(em => em.ProfileId).HasDatabaseName("idx_embeddings_profile");
        });
    }
}
