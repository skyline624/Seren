using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Authentication;

/// <summary>
/// Background service that periodically prunes expired revocation entries
/// from <see cref="InMemoryTokenRevocationStore"/>. Runs every minute which
/// is a good balance between memory pressure and CPU cost for typical JWT
/// lifetimes of 15–60 minutes.
/// </summary>
public sealed class TokenRevocationSweeper : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TokenRevocationSweeper> _logger;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    public TokenRevocationSweeper(IServiceProvider services, ILogger<TokenRevocationSweeper> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            using var scope = _services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ITokenRevocationStore>();

            if (store is InMemoryTokenRevocationStore inMemory)
            {
                var pruned = inMemory.PruneExpired(DateTimeOffset.UtcNow);
                if (pruned > 0)
                {
                    _logger.LogDebug("Pruned {Count} expired revocation entries", pruned);
                }
            }
        }
    }
}
