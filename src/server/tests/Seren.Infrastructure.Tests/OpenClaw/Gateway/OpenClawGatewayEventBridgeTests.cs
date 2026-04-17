using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Seren.Application.OpenClaw.Notifications;
using Seren.Infrastructure.OpenClaw.Gateway;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw.Gateway;

public sealed class OpenClawGatewayEventBridgeTests
{
    private static readonly string[] MethodsSample = { "chat.send" };
    private static readonly string[] EventsSample = { "tick" };

    private static (OpenClawGatewayEventBridge bridge, IPublisher publisher) BuildBridge(IPublisher? overrideWith = null)
    {
        var publisher = overrideWith ?? Substitute.For<IPublisher>();
        var services = new ServiceCollection();
        services.AddSingleton(publisher);
        var sp = services.BuildServiceProvider();
        var bridge = new OpenClawGatewayEventBridge(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger.Instance);
        return (bridge, publisher);
    }

    [Fact]
    public async Task PublishAsync_EmitsRawEventNotification_WithFields()
    {
        var (bridge, publisher) = BuildBridge();
        var payload = JsonSerializer.SerializeToElement(new { hi = "there" });
        var ev = new GatewayEvent("channel:message", payload, Seq: 42, StateVersion: null);

        await bridge.PublishAsync(ev, CancellationToken.None);

        await publisher.Received(1).Publish(
            Arg.Is<OpenClawGatewayRawEventNotification>(n =>
                n.EventName == "channel:message"
                && n.Seq == 42
                && n.Payload!.Value.GetProperty("hi").GetString() == "there"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Swallows_HandlerExceptions()
    {
        // Hand-rolled publisher — setting up an exception on a ValueTask-
        // returning method via NSubstitute triggers CA2012 even under
        // .When().Do(). A concrete type is simpler and clearer.
        var publisher = new ThrowingPublisher();
        var (bridge, _) = BuildBridge(publisher);

        await Should.NotThrowAsync(async () =>
            await bridge.PublishAsync(
                new GatewayEvent("x", null, null, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task PublishReadyAsync_EmitsReadyNotification_WithHelloOkFields()
    {
        var (bridge, publisher) = BuildBridge();

        var hello = new HelloOkPayload(
            Protocol: 3,
            Server: new HelloOkServer("1.2.3", "conn-abc"),
            Features: new HelloOkFeatures(MethodsSample, EventsSample),
            Policy: new HelloOkPolicy(524288, 1048576, 5000),
            CanvasHostUrl: null);

        await bridge.PublishReadyAsync(hello, CancellationToken.None);

        await publisher.Received(1).Publish(
            Arg.Is<OpenClawGatewayReadyNotification>(n =>
                n.ProtocolVersion == 3
                && n.ServerVersion == "1.2.3"
                && n.ConnectionId == "conn-abc"
                && n.TickIntervalMs == 5000
                && n.Methods.Count == 1
                && n.Events.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishDisconnectedAsync_EmitsDisconnectedNotification()
    {
        var (bridge, publisher) = BuildBridge();

        await bridge.PublishDisconnectedAsync("socket exception: reset", true, CancellationToken.None);

        await publisher.Received(1).Publish(
            Arg.Is<OpenClawGatewayDisconnectedNotification>(n =>
                n.Reason == "socket exception: reset" && n.WasHandshakeComplete),
            Arg.Any<CancellationToken>());
    }

    private sealed class ThrowingPublisher : IPublisher
    {
        public ValueTask Publish<TNotification>(
            TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => throw new InvalidOperationException("handler boom");

        public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("handler boom");
    }
}
