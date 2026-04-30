using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seren.Modules.VoxMind.Speakers.Database;

/// <summary>
/// Applies pending EF Core migrations on the speaker database before the
/// host starts accepting traffic. Skipped when the VoxMind module — or
/// the speaker subsystem specifically — is disabled, so config-only
/// deployments don't pay the SQLite open cost.
/// </summary>
/// <remarks>
/// Wired as <see cref="IHostedService"/> in <c>VoxMindModule.Configure</c>.
/// Failures are logged but do not crash the host: the speaker service
/// itself reports <see cref="SpeakerIdentificationOutcome.Unavailable"/>
/// on every call when the DB cannot be reached, which the pipeline
/// already tolerates gracefully.
/// </remarks>
public sealed class SpeakerDbMigrationHostedService : IHostedService
{
    private readonly IDbContextFactory<VoxMindSpeakerDbContext> _dbFactory;
    private readonly IOptions<VoxMindOptions> _options;
    private readonly ILogger<SpeakerDbMigrationHostedService> _logger;

    public SpeakerDbMigrationHostedService(
        IDbContextFactory<VoxMindSpeakerDbContext> dbFactory,
        IOptions<VoxMindOptions> options,
        ILogger<SpeakerDbMigrationHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _dbFactory = dbFactory;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled || !opts.Speakers.Enabled)
        {
            return;
        }

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Speaker database schema is up to date ({DbPath}).",
                opts.Speakers.DbPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to apply speaker database migrations at {DbPath}; speaker recognition will report Unavailable until the issue is resolved.",
                opts.Speakers.DbPath);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
