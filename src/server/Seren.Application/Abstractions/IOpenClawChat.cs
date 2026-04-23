namespace Seren.Application.Abstractions;

/// <summary>
/// Application-layer contract for streaming a chat completion over the
/// persistent OpenClaw WebSocket gateway. Implemented in the infrastructure
/// layer on top of <c>IOpenClawGateway.CallAsync("chat.send", …)</c> plus
/// a per-<c>runId</c> subscription to the incoming <c>"chat"</c> events.
/// </summary>
/// <remarks>
/// The gateway sends each delta as the full text buffered so far (not the
/// new fragment); implementations are expected to convert the cumulative
/// text into incremental deltas before yielding <see cref="ChatStreamDelta"/>
/// values, so callers can stay transport-agnostic.
/// </remarks>
public interface IOpenClawChat
{
    /// <summary>
    /// Start a chat run on the gateway. Returns the <c>runId</c> the gateway
    /// will stamp on every incoming <c>"chat"</c> event. The caller should
    /// immediately call <see cref="SubscribeAsync"/> with the same id.
    /// </summary>
    /// <param name="sessionKey">OpenClaw session identifier (used to preserve conversation context upstream).</param>
    /// <param name="message">User text to send.</param>
    /// <param name="agentId">Optional agent/model identifier; forwarded to the gateway as part of the session context.</param>
    /// <param name="idempotencyKey">
    /// Optional client-minted id used both as OpenClaw's <c>idempotencyKey</c>
    /// and as the returned <c>runId</c>. Passing the client message id lets
    /// the UI know the runId up-front — important for a Stop button that
    /// targets a specific run. When <c>null</c> the implementation mints a
    /// fresh GUID. A retry with the same key returns <c>status:"in_flight"</c>
    /// upstream and transparently resubscribes to the existing run.
    /// </param>
    /// <param name="cancellationToken">Cancellation propagated to the underlying RPC call.</param>
    Task<string> StartAsync(
        string sessionKey,
        string message,
        string? agentId,
        string? idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pin (or clear) a model override on a session. OpenClaw's
    /// <c>chat.send</c> RPC does not accept a per-request model parameter:
    /// the gateway resolves the model from the session's configured
    /// override, falling back to the agent's default. Call this before
    /// <see cref="StartAsync"/> whenever the UI selection changes so the
    /// next turn routes to the intended provider/model.
    /// </summary>
    /// <param name="sessionKey">Target session.</param>
    /// <param name="model">
    /// Fully-qualified <c>provider/model</c> key (e.g. <c>ollama/seren-qwen:latest</c>).
    /// Pass <c>null</c> to clear the override and let the gateway fall back
    /// to its configured default.
    /// </param>
    /// <param name="cancellationToken">Cancellation propagated to the RPC call.</param>
    Task PinSessionModelAsync(
        string sessionKey,
        string? model,
        CancellationToken cancellationToken);

    /// <summary>
    /// Subscribe to a run started by <see cref="StartAsync"/>. The enumerator
    /// yields <see cref="ChatStreamDelta"/> items until a terminal state
    /// arrives (end / aborted / error). An error state throws; an abort
    /// raises <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="runId">Run identifier returned by <see cref="StartAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation — unregisters the run when it fires.</param>
    IAsyncEnumerable<ChatStreamDelta> SubscribeAsync(
        string runId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ask the gateway to abort a run in progress. Used by the user-facing
    /// Stop button and by the server-side idle/total timeout safety net.
    /// Safe to call for an already-finished run — implementations swallow
    /// <c>NOT_FOUND</c> so late aborts race-free.
    /// </summary>
    /// <param name="sessionKey">Session the run belongs to.</param>
    /// <param name="runId">Run identifier returned by <see cref="StartAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation for the underlying RPC.</param>
    Task AbortAsync(
        string sessionKey,
        string runId,
        CancellationToken cancellationToken);
}

/// <summary>
/// A single incremental piece of a streaming chat completion. Either
/// <see cref="Content"/> contains newly-produced text, or
/// <see cref="FinishReason"/> is set to signal a clean end-of-stream.
/// </summary>
public sealed record ChatStreamDelta(string? Content, string? FinishReason);
