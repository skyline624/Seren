using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Infrastructure.OpenClaw.Gateway;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// <see cref="IOpenClawChat"/> implementation layered on the gateway WebSocket.
/// Orchestrates the <c>chat.send</c> RPC, per-run subscription to incoming
/// <c>"chat"</c> events, and cumulative-to-delta text conversion so callers
/// receive incremental fragments just like the former SSE client.
/// </summary>
public sealed class OpenClawGatewayChatClient : IOpenClawChat
{
    private readonly IOpenClawGateway _gateway;
    private readonly OpenClawChatStreamDispatcher _dispatcher;
    private readonly OpenClawOptions _options;
    private readonly ChatStreamOptions _streamOptions;
    private readonly ILogger<OpenClawGatewayChatClient> _logger;

    public OpenClawGatewayChatClient(
        IOpenClawGateway gateway,
        OpenClawChatStreamDispatcher dispatcher,
        IOptions<OpenClawOptions> options,
        IOptions<ChatStreamOptions> streamOptions,
        ILogger<OpenClawGatewayChatClient> logger)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(streamOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _gateway = gateway;
        _dispatcher = dispatcher;
        _options = options.Value;
        _streamOptions = streamOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PinSessionModelAsync(
        string sessionKey, string? model, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionKey);

        var normalized = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        var paramsElement = JsonSerializer.SerializeToElement(
            new SessionsPatchModelParams(sessionKey, normalized),
            OpenClawGatewayJsonContext.Default.SessionsPatchModelParams);

        try
        {
            await _gateway.CallAsync(
                method: "sessions.patch",
                parameters: paramsElement,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "sessions.patch → sessionKey={SessionKey} model={Model}",
                sessionKey, normalized ?? "<cleared>");
        }
        catch (OpenClawGatewayException ex)
        {
            // Model pinning is best-effort: if the gateway rejects the patch
            // (e.g. the session doesn't exist yet), log and let chat.send
            // proceed on the agent's default model. The alternative would be
            // to silently swallow the user's intent, which is worse UX than
            // falling back to a working (if not ideal) response.
            _logger.LogWarning(
                "sessions.patch rejected by gateway ({Code}: {Reason}); proceeding without model pin.",
                ex.Code, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<string> StartAsync(
        string sessionKey,
        string message,
        string? agentId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionKey);
        ArgumentException.ThrowIfNullOrEmpty(message);

        // OpenClaw requires a unique, non-empty idempotencyKey; it becomes the
        // run id. Prefer the client-supplied id (= clientMessageId) so the UI
        // knows the runId up-front for its Stop button; fall back to a fresh
        // GUID when the caller didn't provide one.
        var effectiveKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : idempotencyKey;

        var timeoutMs = (int)_streamOptions.TotalTimeout.TotalMilliseconds;
        var paramsElement = JsonSerializer.SerializeToElement(
            new ChatSendParams(sessionKey, message, effectiveKey, timeoutMs),
            OpenClawGatewayJsonContext.Default.ChatSendParams);

        _logger.LogDebug(
            "chat.send → sessionKey={SessionKey} agentId={AgentId} idempotencyKey={Key} timeoutMs={TimeoutMs}",
            sessionKey, agentId ?? _options.DefaultAgentId, effectiveKey, timeoutMs);

        var payload = await _gateway.CallAsync(
            method: "chat.send",
            parameters: paramsElement,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var result = payload.Deserialize(OpenClawGatewayJsonContext.Default.ChatSendResult)
            ?? throw new OpenClawGatewayException(
                code: "chat.send.invalid",
                message: "Gateway returned an empty chat.send response.");

        if (string.IsNullOrEmpty(result.RunId))
        {
            throw new OpenClawGatewayException(
                code: "chat.send.invalid",
                message: "Gateway accepted chat.send but returned no runId.");
        }

        return result.RunId;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatStreamDelta> SubscribeAsync(
        string runId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);

        var reader = _dispatcher.Register(runId);
        var previousText = string.Empty;
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var ev))
                {
                    switch (ev.State)
                    {
                        case ChatEventState.Delta:
                            var delta = ExtractIncrement(ev, ref previousText);
                            if (!string.IsNullOrEmpty(delta))
                            {
                                yield return new ChatStreamDelta(delta, FinishReason: null);
                            }
                            break;

                        case ChatEventState.Final:
                            var tail = ExtractIncrement(ev, ref previousText);
                            if (!string.IsNullOrEmpty(tail))
                            {
                                yield return new ChatStreamDelta(tail, FinishReason: null);
                            }
                            yield return new ChatStreamDelta(
                                Content: null,
                                FinishReason: ev.StopReason ?? "stop");
                            yield break;

                        case ChatEventState.Aborted:
                            throw new OperationCanceledException(
                                "Chat run was aborted by the gateway.",
                                cancellationToken.IsCancellationRequested
                                    ? cancellationToken
                                    : CancellationToken.None);

                        case ChatEventState.Error:
                            throw new OpenClawGatewayException(
                                code: ev.ErrorKind ?? "chat.error",
                                message: ev.ErrorMessage ?? "Gateway reported a chat error.");

                        default:
                            _logger.LogDebug(
                                "Unknown chat event state {State} for run {RunId}; ignoring.",
                                ev.State, runId);
                            break;
                    }
                }
            }
        }
        finally
        {
            _dispatcher.Unregister(runId);
        }
    }

    /// <inheritdoc />
    public async Task AbortAsync(
        string sessionKey, string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionKey);
        ArgumentException.ThrowIfNullOrEmpty(runId);

        var paramsElement = JsonSerializer.SerializeToElement(
            new ChatAbortParams(sessionKey, runId),
            OpenClawGatewayJsonContext.Default.ChatAbortParams);

        try
        {
            await _gateway.CallAsync(
                method: "chat.abort",
                parameters: paramsElement,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "chat.abort → sessionKey={SessionKey} runId={RunId}",
                sessionKey, runId);
        }
        catch (OpenClawGatewayException ex)
        {
            // The run may have completed (or never existed) by the time the
            // abort lands — treat NOT_FOUND-style codes as benign so the
            // user-facing Stop button never surfaces a confusing error.
            _logger.LogDebug(
                "chat.abort no-op for runId={RunId}: gateway returned {Code} ({Message})",
                runId, ex.Code, ex.Message);
        }
    }

    private static string ExtractIncrement(ChatEventPayload ev, ref string previousText)
    {
        var merged = ev.Message?.Content
            ?.Where(c => c.Type == "text" || c.Type is null)
            .Select(c => c.Text)
            .Where(t => !string.IsNullOrEmpty(t))
            .Aggregate(
                new System.Text.StringBuilder(),
                (sb, t) => sb.Append(t),
                sb => sb.ToString())
            ?? string.Empty;

        // Common case for a `final` echoing the last `delta` verbatim: nothing
        // new to emit. Without this early-out, the next branch would treat it
        // as "server re-wrote the prefix" and re-emit the full text, duplicating
        // every chunk on the wire.
        if (merged == previousText)
        {
            return string.Empty;
        }

        if (merged.Length < previousText.Length || !merged.StartsWith(previousText, StringComparison.Ordinal))
        {
            // The server re-wrote the prefix (rare but possible when it strips
            // a control token after buffering). Treat the whole merged text as
            // the new delta so no output is lost.
            previousText = merged;
            return merged;
        }

        var delta = merged[previousText.Length..];
        previousText = merged;
        return delta;
    }
}
