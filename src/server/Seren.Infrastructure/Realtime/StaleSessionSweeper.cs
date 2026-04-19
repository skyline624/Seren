using System.Net.WebSockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Domain.Abstractions;

namespace Seren.Infrastructure.Realtime;

/// <summary>
/// Background service that periodically scans the peer registry and evicts
/// peers whose <see cref="Domain.Entities.Peer.LastHeartbeatAt"/> exceeds
/// <see cref="SerenHubOptions.ReadTimeoutSeconds"/>. Stale connections are
/// closed gracefully before being unregistered.
/// </summary>
public sealed class StaleSessionSweeper : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(10);

    private readonly IPeerRegistry _peers;
    private readonly IWebSocketConnectionRegistry _connections;
    private readonly IOptions<SerenHubOptions> _options;
    private readonly ILogger<StaleSessionSweeper> _logger;

    public StaleSessionSweeper(
        IPeerRegistry peers,
        IWebSocketConnectionRegistry connections,
        IOptions<SerenHubOptions> options,
        ILogger<StaleSessionSweeper> logger)
    {
        _peers = peers;
        _connections = connections;
        _options = options;
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

            var timeout = TimeSpan.FromSeconds(_options.Value.ReadTimeoutSeconds);
            var cutoff = DateTimeOffset.UtcNow - timeout;
            var snapshot = _peers.Snapshot();
            var evicted = 0;

            foreach (var peer in snapshot)
            {
                if (peer.LastHeartbeatAt >= cutoff)
                {
                    continue;
                }

                _logger.LogInformation(
                    "Evicting stale peer {PeerId} (last heartbeat {Ago}s ago)",
                    peer.Id, (DateTimeOffset.UtcNow - peer.LastHeartbeatAt).TotalSeconds);

                // Close the WebSocket gracefully
                if (_connections.TryGet(peer.Id, out var socket) && socket is not null)
                {
                    try
                    {
                        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                        {
                            await socket.CloseAsync(
                                WebSocketCloseStatus.PolicyViolation,
                                "seren: session timed out",
                                CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    catch (WebSocketException)
                    {
                        // best-effort close
                    }
                }

                _connections.TryUnregister(peer.Id);
                _peers.Remove(peer.Id);
                evicted++;
            }

            if (evicted > 0)
            {
                _logger.LogInformation("Evicted {Count} stale peer(s)", evicted);
            }
        }
    }
}
