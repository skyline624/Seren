using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Tracks in-flight gateway RPC calls and correlates incoming response
/// frames with the pending <see cref="TaskCompletionSource{TResult}"/>.
/// The actual send is delegated to a function supplied by the orchestrator —
/// this class never touches the socket directly, which keeps it
/// straightforwardly unit-testable.
/// </summary>
internal sealed class OpenClawGatewayRpc : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, PendingCall> _pending = new(StringComparer.Ordinal);
    private readonly ILogger _logger;
    private readonly TimeSpan _defaultTimeout;
    private int _disposed;

    public OpenClawGatewayRpc(ILogger logger, TimeSpan defaultTimeout)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (defaultTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultTimeout),
                "Default RPC timeout must be strictly positive.");
        }
        _logger = logger;
        _defaultTimeout = defaultTimeout;
    }

    /// <summary>Number of currently-pending calls. Exposed for tests + diagnostics.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// Send a gateway RPC request and await the matching response.
    /// </summary>
    /// <param name="sendAsync">Delegate that ships the serialized request on the wire.</param>
    /// <param name="method">Gateway method name.</param>
    /// <param name="params">Optional params — serialized to JSON.</param>
    /// <param name="timeout">Per-call timeout; falls back to the default when null.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    public async Task<JsonElement> CallAsync(
        Func<GatewayRequest, CancellationToken, Task> sendAsync,
        string method,
        object? parameters,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sendAsync);
        ArgumentException.ThrowIfNullOrEmpty(method);
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        var id = Guid.NewGuid().ToString("N");
        var paramsElement = SerializeParams(parameters);
        var request = new GatewayRequest(id, method, paramsElement);

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var effectiveTimeout = timeout ?? _defaultTimeout;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(effectiveTimeout);

        var pending = new PendingCall(tcs, method);
        if (!_pending.TryAdd(id, pending))
        {
            // Infinitesimally unlikely collision; fail fast so callers retry.
            throw new InvalidOperationException(
                $"RPC id collision detected for method '{method}'. Retry the call.");
        }

        await using var registration = linked.Token.Register(
            static state =>
            {
                var (self, callId, tcsLocal) = ((OpenClawGatewayRpc, string, TaskCompletionSource<JsonElement>))state!;
                if (self._pending.TryRemove(callId, out _))
                {
                    tcsLocal.TrySetCanceled();
                }
            },
            (this, id, tcs)).ConfigureAwait(false);

        try
        {
            await sendAsync(request, linked.Token).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Resolve a pending call from an inbound <see cref="GatewayResponse"/>.
    /// Returns <c>false</c> when no call matches the id (e.g. server echoed
    /// a response to a different client or we cancelled before it arrived).
    /// </summary>
    public bool CompletePending(GatewayResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!_pending.TryRemove(response.Id, out var pending))
        {
            return false;
        }

        if (!response.Ok)
        {
            var ex = new OpenClawGatewayException(
                code: response.Error?.Code ?? "rpc.error",
                message: response.Error?.Message ?? $"Gateway rejected call '{pending.Method}'.",
                retryable: response.Error?.Retryable,
                retryAfterMs: response.Error?.RetryAfterMs);
            pending.Completion.TrySetException(ex);
            return true;
        }

        var payload = response.Payload ?? default;
        pending.Completion.TrySetResult(payload);
        return true;
    }

    /// <summary>
    /// Fail every pending call. Invoked by the orchestrator when the socket
    /// drops — callers then observe a single terminal exception instead of
    /// hanging until their own cancellation token fires.
    /// </summary>
    public void FailAllPending(Exception reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var snapshot = _pending.ToArray();
        foreach (var kvp in snapshot)
        {
            if (_pending.TryRemove(kvp.Key, out var pending))
            {
                pending.Completion.TrySetException(reason);
            }
        }
        if (snapshot.Length > 0)
        {
            _logger.LogDebug(
                "Failed {Count} pending RPC call(s) because the gateway connection dropped: {Reason}",
                snapshot.Length, reason.Message);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }
        FailAllPending(new ObjectDisposedException(nameof(OpenClawGatewayRpc)));
        return ValueTask.CompletedTask;
    }

    private static JsonElement? SerializeParams(object? parameters)
    {
        if (parameters is null)
        {
            return null;
        }

        if (parameters is JsonElement element)
        {
            return element;
        }

        // For Application-layer callers we accept arbitrary object graphs.
        // System.Text.Json handles them with the ambient reflection-based
        // serializer — safe for a backend process (Seren is not AOT-published)
        // and keeps IOpenClawGateway caller-friendly without requiring each
        // caller to build their own source-gen context.
        return JsonSerializer.SerializeToElement(parameters);
    }

    private sealed record PendingCall(TaskCompletionSource<JsonElement> Completion, string Method);
}
