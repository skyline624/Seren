using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Watchdog that closes the socket with code <c>4000 tick-timeout</c> when
/// the gateway stops emitting frames for longer than the negotiated tick
/// interval multiplied by a configurable grace factor. Mirrors the
/// reference TypeScript client (<c>src/gateway/client.ts</c>, tick timer).
/// </summary>
internal sealed class OpenClawGatewayTickMonitor : IAsyncDisposable
{
    private readonly TimeSpan _checkPeriod;
    private readonly TimeSpan _staleAfter;
    private readonly Func<WebSocketCloseStatus, string, CancellationToken, Task> _closeAsync;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;
    private long _lastActivityTicks;
    private int _disposed;

    public OpenClawGatewayTickMonitor(
        int tickIntervalMs,
        double graceMultiplier,
        Func<WebSocketCloseStatus, string, CancellationToken, Task> closeAsync,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(closeAsync);
        ArgumentNullException.ThrowIfNull(logger);
        if (tickIntervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickIntervalMs),
                "Tick interval must be strictly positive.");
        }
        if (!(graceMultiplier > 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(graceMultiplier),
                "Grace multiplier must be greater than 1.0 so normal jitter doesn't trip the watchdog.");
        }

        _closeAsync = closeAsync;
        _logger = logger;
        _staleAfter = TimeSpan.FromMilliseconds(tickIntervalMs * graceMultiplier);
        // Poll at roughly the tick interval — fast enough to react within
        // one extra tick window, slow enough not to burn CPU.
        _checkPeriod = TimeSpan.FromMilliseconds(Math.Max(100, tickIntervalMs / 2));
        _lastActivityTicks = Environment.TickCount64;
        _loopTask = Task.Run(LoopAsync);
    }

    /// <summary>Called on every inbound frame — including tick events — to reset the watchdog.</summary>
    public void OnFrameReceived()
    {
        Interlocked.Exchange(ref _lastActivityTicks, Environment.TickCount64);
    }

    private async Task LoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_checkPeriod, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var elapsed = TimeSpan.FromMilliseconds(
                    Environment.TickCount64 - Interlocked.Read(ref _lastActivityTicks));

                if (elapsed <= _staleAfter)
                {
                    continue;
                }

                _logger.LogWarning(
                    "OpenClaw gateway tick timeout: no activity for {ElapsedMs}ms (threshold={ThresholdMs}ms). Closing socket with code 4000.",
                    (long)elapsed.TotalMilliseconds, (long)_staleAfter.TotalMilliseconds);

                try
                {
                    await _closeAsync(
                        (WebSocketCloseStatus)4000,
                        "tick timeout",
                        _cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Error while closing gateway socket on tick timeout");
                }

                return; // One-shot: after firing, the read loop will exit and a fresh monitor starts next session.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenClaw gateway tick monitor crashed");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Already cancelled — safe to swallow.
        }

        try
        {
            await _loopTask.ConfigureAwait(false);
        }
        catch
        {
            // Surface errors via the logger inside LoopAsync; Dispose never throws.
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
