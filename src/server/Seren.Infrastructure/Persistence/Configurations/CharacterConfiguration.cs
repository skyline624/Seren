using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Seren.Domain.Entities;

namespace Seren.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for <see cref="Character"/>.
/// Maps the immutable record to the <c>Characters</c> table.
/// </summary>
public sealed class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Characters");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.SystemPrompt)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(c => c.VrmAssetPath)
            .HasMaxLength(500);

        builder.Property(c => c.Voice)
            .HasMaxLength(100);

        builder.Property(c => c.AgentId)
            .HasMaxLength(200);

        builder.Property(c => c.CreatedAt)
            .HasConversion(
                v => v.UtcDateTime,
                v => new DateTimeOffset(v, TimeSpan.Zero));

        builder.Property(c => c.UpdatedAt)
            .HasConversion(
                v => v.UtcDateTime,
                v => new DateTimeOffset(v, TimeSpan.Zero));

        builder.HasIndex(c => c.IsActive)
            .HasDatabaseName("idx_characters_is_active");
    }
}
