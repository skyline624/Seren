using Microsoft.EntityFrameworkCore;
using Seren.Domain.Entities;

namespace Seren.Infrastructure.Persistence;

/// <summary>
/// EF Core database context for Seren. Uses SQLite in development,
/// replaceable with PostgreSQL for production via connection string swap.
/// </summary>
public sealed class SerenDbContext : DbContext
{
    public SerenDbContext(DbContextOptions<SerenDbContext> options)
        : base(options)
    {
    }

    public DbSet<Character> Characters => Set<Character>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SerenDbContext).Assembly);
    }
}
