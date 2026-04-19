using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Routes inbound OpenClaw <c>"chat"</c> events to per-run subscribers.
/// A single gateway WebSocket produces events for every concurrent chat run,
/// each correlated by <see cref="ChatEventPayload.RunId"/>. Subscribers
/// (typically <see cref="OpenClawGatewayChatClient"/>) register a run after
/// issuing <c>chat.send</c> and read deltas from the returned channel until
/// the terminal state (<c>final</c> / <c>aborted</c> / <c>error</c>) arrives.
/// </summary>
/// <remarks>
/// Registered as a singleton. The read loop (<see cref="OpenClawWebSocketClient"/>)
/// reaches this class through <see cref="OpenClawEventRouter"/>; the
/// <see cref="Dispatch"/> path must never block, hence unbounded channels.
/// <para/>
/// Orphan subscriptions (registered but never reached a terminal state) are
/// purged after <see cref="DefaultRunTtl"/> so memory doesn't grow if a
/// caller cancels without draining.
/// </remarks>
public sealed class OpenClawChatStreamDispatcher : IAsyncDisposable
{
    /// <summary>Default TTL before an orphan run is evicted from the registry.</summary>
    public static readonly TimeSpan DefaultRunTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, ChatRun> _runs = new(StringComparer.Ordinal);
    private readonly ILogger<OpenClawChatStreamDispatcher> _logger;
    private readonly TimeSpan _runTtl;
    private readonly CancellationTokenSource _sweeperCts = new();
    private readonly Task _sweeperTask;
    private int _disposed;

    public OpenClawChatStreamDispatcher(ILogger<OpenClawChatStreamDispatcher> logger)
        : this(logger, DefaultRunTtl, sweepInterval: TimeSpan.FromMinutes(1))
    {
    }

    // Test-friendly overload so unit tests can drive faster TTLs.
    internal OpenClawChatStreamDispatcher(
        ILogger<OpenClawChatStreamDispatcher> logger,
        TimeSpan runTtl,
        TimeSpan sweepInterval)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (runTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(runTtl), "Run TTL must be strictly positive.");
        }
        if (sweepInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sweepInterval), "Sweep interval must be strictly positive.");
        }

        _logger = logger;
        _runTtl = runTtl;
        _sweeperTask = Task.Run(() => SweepLoopAsync(sweepInterval, _sweeperCts.Token));
    }

    /// <summary>Number of currently-registered runs. Exposed for tests + diagnostics.</summary>
    public int RegisteredCount => _runs.Count;

    /// <summary>
    /// Register a run so that subsequent events with matching
    /// <see cref="ChatEventPayload.RunId"/> are delivered to the returned
    /// reader. The reader completes on terminal state or cancellation.
    /// </summary>
    /// <exception cref="InvalidOperationException">A run with the same id is already registered.</exception>
    internal ChannelReader<ChatEventPayload> Register(string runId)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        var channel = Channel.CreateUnbounded<ChatEventPayload>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var run = new ChatRun(channel, DateTimeOffset.UtcNow);
        if (!_runs.TryAdd(runId, run))
        {
            throw new InvalidOperationException(
                $"Chat run '{runId}' is already registered. Use a fresh idempotencyKey.");
        }

        return channel.Reader;
    }

    /// <summary>
    /// Unregister a run, typically on caller cancellation before a terminal
    /// state was received. Completes the channel so readers return cleanly.
    /// Safe to call with an unknown id.
    /// </summary>
    internal void Unregister(string runId)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        if (_runs.TryRemove(runId, out var run))
        {
            run.Channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Route an inbound event to the matching run. Returns <c>true</c> when
    /// a subscription consumed the event, <c>false</c> when the run is
    /// unknown (either cancelled or never registered). Terminal states
    /// (<c>final</c> / <c>aborted</c> / <c>error</c>) complete the channel
    /// and remove the registration.
    /// </summary>
    internal bool Dispatch(ChatEventPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!_runs.TryGetValue(payload.RunId, out var run))
        {
            _logger.LogDebug(
                "Chat event for unknown run {RunId} (state={State}); dropping.",
                payload.RunId, payload.State);
            return false;
        }

        if (!run.Channel.Writer.TryWrite(payload))
        {
            _logger.LogWarning(
                "Chat event channel rejected write for run {RunId} (state={State}).",
                payload.RunId, payload.State);
            return false;
        }

        if (ChatEventState.IsTerminal(payload.State))
        {
            // Remove before completing so a late-arriving duplicate doesn't
            // hit an already-completed channel.
            _runs.TryRemove(payload.RunId, out _);
            run.Channel.Writer.TryComplete();
        }

        return true;
    }

    private async Task SweepLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                SweepOnce(DateTimeOffset.UtcNow);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose.
        }
    }

    internal void SweepOnce(DateTimeOffset now)
    {
        var cutoff = now - _runTtl;
        foreach (var kvp in _runs)
        {
            if (kvp.Value.RegisteredAt <= cutoff && _runs.TryRemove(kvp.Key, out var run))
            {
                _logger.LogWarning(
                    "Evicting orphan chat run {RunId} after {TtlSeconds}s without terminal state.",
                    kvp.Key, _runTtl.TotalSeconds);
                run.Channel.Writer.TryComplete(
                    new OpenClawGatewayException(
                        code: "chat.stream.orphan",
                        message: $"Chat run '{kvp.Key}' expired without a terminal state."));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _sweeperCts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _sweeperTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }
        _sweeperCts.Dispose();

        // Drop any remaining subscribers so their readers observe completion.
        foreach (var kvp in _runs)
        {
            kvp.Value.Channel.Writer.TryComplete(
                new OpenClawGatewayException("chat.stream.shutdown", "Dispatcher disposed."));
        }
        _runs.Clear();
    }

    private sealed record ChatRun(Channel<ChatEventPayload> Channel, DateTimeOffset RegisteredAt);
}
