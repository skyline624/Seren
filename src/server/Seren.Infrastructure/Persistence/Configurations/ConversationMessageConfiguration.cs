using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Seren.Domain.Entities;

namespace Seren.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for <see cref="ConversationMessage"/>.
/// </summary>
public sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ConversationMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(m => m.Content)
            .IsRequired();

        builder.HasIndex(m => m.SessionId)
            .HasDatabaseName("idx_conversation_session");

        builder.Property(m => m.CreatedAt)
            .HasConversion(
                v => v.UtcDateTime,
                v => new DateTimeOffset(v, TimeSpan.Zero));

        builder.HasIndex(m => m.CreatedAt)
            .HasDatabaseName("idx_conversation_created");
    }
}
