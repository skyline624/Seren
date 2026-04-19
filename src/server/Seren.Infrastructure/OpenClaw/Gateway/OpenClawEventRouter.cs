using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Seren.Application.OpenClaw.Notifications;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Dispatches <see cref="OpenClawGatewayRawEventNotification"/> instances
/// into either the chat stream dispatcher (for <c>"chat"</c> events) or
/// domain-typed Mediator notifications the Application layer consumes
/// (session messages, approvals, agent events). Unknown events are logged
/// at debug level and otherwise ignored — they stay available as raw
/// notifications for any future handler that wants to opt in.
/// </summary>
/// <remarks>
/// Registered as a Mediator notification handler (scoped per Mediator's
/// source-gen), but the chat dispatcher it delegates to is a singleton so
/// state survives across notifications. Exceptions thrown by a downstream
/// handler bubble up — <see cref="OpenClawGatewayEventBridge"/> already
/// catches and logs them so the gateway read loop stays alive.
/// </remarks>
public sealed class OpenClawEventRouter : INotificationHandler<OpenClawGatewayRawEventNotification>
{
    // Chat event (streaming deltas + final/aborted/error).
    private const string EventNameChat = "chat";

    // Session lifecycle.
    private const string EventNameSessionMessage = "session.message";

    // Approvals — both exec and plugin flavors share a common shape.
    private const string EventNameExecApprovalRequested = "exec.approval.requested";
    private const string EventNameExecApprovalResolved = "exec.approval.resolved";
    private const string EventNamePluginApprovalRequested = "plugin.approval.requested";
    private const string EventNamePluginApprovalResolved = "plugin.approval.resolved";

    // Agent lifecycle + tool invocation.
    private const string EventNameAgent = "agent";

    private readonly OpenClawChatStreamDispatcher _chatDispatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OpenClawEventRouter> _logger;

    public OpenClawEventRouter(
        OpenClawChatStreamDispatcher chatDispatcher,
        IServiceScopeFactory scopeFactory,
        ILogger<OpenClawEventRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(chatDispatcher);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _chatDispatcher = chatDispatcher;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async ValueTask Handle(
        OpenClawGatewayRawEventNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        switch (notification.EventName)
        {
            case EventNameChat:
                DispatchChat(notification.Payload);
                return;

            case EventNameSessionMessage:
                await PublishInScopeAsync(
                    TryBuildSessionMessage(notification.Payload, notification.Seq),
                    cancellationToken).ConfigureAwait(false);
                return;

            case EventNameExecApprovalRequested:
                await PublishInScopeAsync(
                    TryBuildApprovalRequested(notification.Payload, kind: "exec"),
                    cancellationToken).ConfigureAwait(false);
                return;

            case EventNamePluginApprovalRequested:
                await PublishInScopeAsync(
                    TryBuildApprovalRequested(notification.Payload, kind: "plugin"),
                    cancellationToken).ConfigureAwait(false);
                return;

            case EventNameExecApprovalResolved:
                await PublishInScopeAsync(
                    TryBuildApprovalResolved(notification.Payload, kind: "exec"),
                    cancellationToken).ConfigureAwait(false);
                return;

            case EventNamePluginApprovalResolved:
                await PublishInScopeAsync(
                    TryBuildApprovalResolved(notification.Payload, kind: "plugin"),
                    cancellationToken).ConfigureAwait(false);
                return;

            case EventNameAgent:
                await PublishInScopeAsync(
                    TryBuildAgentEvent(notification.Payload, notification.Seq),
                    cancellationToken).ConfigureAwait(false);
                return;

            default:
                _logger.LogDebug(
                    "OpenClaw event {EventName} has no typed router; raw notification remains available.",
                    notification.EventName);
                return;
        }
    }

    private void DispatchChat(JsonElement? payload)
    {
        if (payload is null)
        {
            _logger.LogWarning("OpenClaw 'chat' event carried no payload; dropping.");
            return;
        }

        ChatEventPayload? parsed;
        try
        {
            parsed = payload.Value.Deserialize(OpenClawGatewayJsonContext.Default.ChatEventPayload);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize OpenClaw chat event payload.");
            return;
        }

        if (parsed is null || string.IsNullOrEmpty(parsed.RunId))
        {
            _logger.LogWarning("OpenClaw chat event missing runId; dropping.");
            return;
        }

        _chatDispatcher.Dispatch(parsed);
    }

    private SessionMessageReceivedNotification? TryBuildSessionMessage(JsonElement? payload, long? outerSeq)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } obj)
        {
            return null;
        }

        var sessionKey = GetString(obj, "sessionKey");
        if (string.IsNullOrEmpty(sessionKey))
        {
            _logger.LogDebug("session.message without sessionKey; dropping.");
            return null;
        }

        // Message can be nested (`message: {...}`) or inlined at top level;
        // upstream has varied historically, so try both.
        var messageObj = obj.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object
            ? m
            : obj;

        var role = GetString(messageObj, "role") ?? "user";
        var content = ExtractContent(messageObj);
        var timestamp = GetInt64(messageObj, "ts") ?? GetInt64(messageObj, "timestamp");
        var author = GetString(messageObj, "author");
        var channel = GetString(messageObj, "channel") ?? GetString(obj, "channel");
        var seq = outerSeq ?? GetInt64(obj, "messageSeq");

        return new SessionMessageReceivedNotification(
            SessionKey: sessionKey,
            Role: role,
            Content: content,
            Timestamp: timestamp,
            Author: author,
            Channel: channel,
            Seq: seq);
    }

    private ApprovalRequestedNotification? TryBuildApprovalRequested(JsonElement? payload, string kind)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } obj)
        {
            return null;
        }

        var id = GetString(obj, "id");
        if (string.IsNullOrEmpty(id))
        {
            _logger.LogDebug("{Kind} approval.requested without id; dropping.", kind);
            return null;
        }

        var request = obj.TryGetProperty("request", out var r) && r.ValueKind == JsonValueKind.Object
            ? r
            : obj;

        var title = GetString(request, "displayName")
                 ?? GetString(request, "title")
                 ?? GetString(request, "command")
                 ?? id;
        var summary = GetString(request, "description") ?? GetString(request, "summary");
        var command = GetString(request, "command");
        var createdAt = GetInt64(obj, "createdAtMs");
        var expiresAt = GetInt64(obj, "expiresAtMs");
        var sourceChannel = GetString(obj, "turnSourceChannel")
                         ?? GetString(request, "turnSourceChannel");

        return new ApprovalRequestedNotification(
            Id: id,
            Kind: kind,
            Title: title,
            Summary: summary,
            Command: command,
            CreatedAtMs: createdAt,
            ExpiresAtMs: expiresAt,
            SourceChannel: sourceChannel);
    }

    private ApprovalResolvedNotification? TryBuildApprovalResolved(JsonElement? payload, string kind)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } obj)
        {
            return null;
        }

        var id = GetString(obj, "id");
        var decision = GetString(obj, "decision");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(decision))
        {
            _logger.LogDebug("{Kind} approval.resolved missing id/decision; dropping.", kind);
            return null;
        }

        return new ApprovalResolvedNotification(
            Id: id,
            Kind: kind,
            Decision: decision,
            ResolvedBy: GetString(obj, "resolvedBy"),
            ResolvedAtMs: GetInt64(obj, "resolvedAtMs"));
    }

    private AgentEventNotification? TryBuildAgentEvent(JsonElement? payload, long? outerSeq)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } obj)
        {
            return null;
        }

        var runId = GetString(obj, "runId");
        var stream = GetString(obj, "stream");
        if (string.IsNullOrEmpty(runId) || string.IsNullOrEmpty(stream))
        {
            _logger.LogDebug("agent event missing runId/stream; dropping.");
            return null;
        }

        JsonElement? data = obj.TryGetProperty("data", out var d) ? d.Clone() : null;
        var phase = obj.TryGetProperty("data", out var dp)
                    && dp.ValueKind == JsonValueKind.Object
                    && dp.TryGetProperty("phase", out var p)
                    && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

        return new AgentEventNotification(
            RunId: runId,
            SessionKey: GetString(obj, "sessionKey"),
            Stream: stream,
            Phase: phase,
            Seq: outerSeq ?? GetInt64(obj, "seq"),
            Data: data);
    }

    private async Task PublishInScopeAsync<TNotification>(
        TNotification? notification, CancellationToken cancellationToken)
        where TNotification : class, INotification
    {
        if (notification is null)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
        await publisher.Publish(notification, cancellationToken).ConfigureAwait(false);
    }

    private static string? GetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long? GetInt64(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.TryGetInt64(out var l) ? l : null
            : null;

    private static string ExtractContent(JsonElement messageObj)
    {
        // Upstream sometimes uses `content: "string"`, sometimes
        // `content: [{type:"text", text:"..."}, ...]`; we flatten to plain text.
        if (!messageObj.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                return content.GetString() ?? string.Empty;

            case JsonValueKind.Array:
                var buffer = new System.Text.StringBuilder();
                foreach (var entry in content.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (entry.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    {
                        buffer.Append(t.GetString());
                    }
                }
                return buffer.ToString();

            default:
                return string.Empty;
        }
    }
}
