using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Seren.Modules.VoxMind.Speakers.Database;

/// <summary>
/// Used exclusively by the EF Core CLI tooling (<c>dotnet ef migrations
/// add</c> / <c>dotnet ef database update</c>) to instantiate the speaker
/// context outside of the host's DI graph. The connection string is a
/// throw-away local file — migrations are schema-only, no data is read or
/// written from this design-time path.
/// </summary>
public sealed class VoxMindSpeakerDbContextFactory : IDesignTimeDbContextFactory<VoxMindSpeakerDbContext>
{
    public VoxMindSpeakerDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<VoxMindSpeakerDbContext>();
        builder.UseSqlite("Data Source=voxmind-speakers.design.db");
        return new VoxMindSpeakerDbContext(builder.Options);
    }
}
