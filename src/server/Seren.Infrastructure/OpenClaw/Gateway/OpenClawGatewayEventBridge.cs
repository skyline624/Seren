using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Seren.Application.OpenClaw.Notifications;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Translates inbound gateway event frames into Mediator notifications so
/// Application-layer handlers can react without ever touching
/// Infrastructure. For now a single catch-all
/// <see cref="OpenClawGatewayRawEventNotification"/> is published for every
/// non-tick event — domain-typed notifications (channel, cron, tool) are
/// added incrementally in follow-up PRs.
/// </summary>
/// <remarks>
/// <see cref="IPublisher"/> is registered as scoped by Mediator's source-gen,
/// so we keep a reference to <see cref="IServiceScopeFactory"/> (singleton)
/// and open a fresh scope per publish call. That matches the one-notification-
/// per-scope semantics Application-layer handlers expect.
/// </remarks>
internal sealed class OpenClawGatewayEventBridge
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    public OpenClawGatewayEventBridge(IServiceScopeFactory scopeFactory, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task PublishAsync(GatewayEvent ev, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ev);
        return PublishInScopeAsync(
            new OpenClawGatewayRawEventNotification(ev.Event, ev.Payload, ev.Seq),
            $"gateway event {ev.Event}",
            cancellationToken);
    }

    public Task PublishReadyAsync(HelloOkPayload helloOk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(helloOk);
        var notification = new OpenClawGatewayReadyNotification(
            ProtocolVersion: helloOk.Protocol,
            ServerVersion: helloOk.Server.Version,
            ConnectionId: helloOk.Server.ConnId,
            TickIntervalMs: helloOk.Policy.TickIntervalMs,
            Methods: helloOk.Features.Methods,
            Events: helloOk.Features.Events);
        return PublishInScopeAsync(notification, "OpenClaw gateway-ready notification", cancellationToken);
    }

    public Task PublishDisconnectedAsync(
        string reason, bool wasHandshakeComplete, CancellationToken cancellationToken)
    {
        return PublishInScopeAsync(
            new OpenClawGatewayDisconnectedNotification(reason, wasHandshakeComplete),
            "OpenClaw gateway-disconnected notification",
            cancellationToken);
    }

    private async Task PublishInScopeAsync<TNotification>(
        TNotification notification,
        string diagnosticLabel,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
        try
        {
            await publisher.Publish(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One handler exploding should not take down the gateway read loop.
            _logger.LogError(ex, "Handler threw while processing {Label}", diagnosticLabel);
        }
    }
}
