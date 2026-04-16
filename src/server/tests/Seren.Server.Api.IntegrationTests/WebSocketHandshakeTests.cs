using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Contracts.Json;
using Shouldly;
using Xunit;

namespace Seren.Server.Api.IntegrationTests;

/// <summary>
/// End-to-end handshake tests: a real <see cref="WebSocket"/> client connects to
/// the in-memory <see cref="WebApplicationFactory{Program}"/> test host, sends a
/// <c>module:announce</c>, and verifies that the hub replies with
/// <c>module:announced</c>. Exercises the full Phase 1 stack:
/// middleware → <c>SerenWebSocketSessionProcessor</c> → Mediator → handler → hub → socket.
/// </summary>
public sealed class WebSocketHandshakeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebSocketHandshakeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task NewConnection_ShouldReceiveTransportHelloFrame()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        // act
        using var socket = await client.ConnectAsync(uri, ct);
        var envelope = await ReceiveEnvelopeAsync(socket, ct);

        // assert
        envelope.Type.ShouldBe(EventTypes.TransportHello);
        envelope.Metadata.Source.PluginId.ShouldBe("seren.hub");

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    [Fact]
    public async Task Announce_ShouldReceiveAnnouncedEchoWithSameIdentity()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        using var socket = await client.ConnectAsync(uri, ct);

        // drain the hello frame
        _ = await ReceiveEnvelopeAsync(socket, ct);

        var announce = new WebSocketEnvelope
        {
            Type = EventTypes.ModuleAnnounce,
            Data = JsonSerializer.SerializeToElement(
                new AnnouncePayload
                {
                    Identity = new ModuleIdentityDto
                    {
                        Id = "stage-web-01",
                        PluginId = "stage-web",
                        Version = "0.1.0",
                    },
                    Name = "Seren Web",
                },
                SerenJsonContext.Default.AnnouncePayload),
            Metadata = new EventMetadata
            {
                Source = new ModuleIdentityDto
                {
                    Id = "stage-web-01",
                    PluginId = "stage-web",
                },
                Event = new EventIdentity { Id = "evt-1" },
            },
        };

        // act
        await SendEnvelopeAsync(socket, announce, ct);
        var response = await ReceiveEnvelopeAsync(socket, ct);

        // assert
        response.Type.ShouldBe(EventTypes.ModuleAnnounced);
        response.Metadata.Event.ParentId.ShouldBe("evt-1");

        var announced = response.Data.Deserialize(SerenJsonContext.Default.AnnouncedPayload);
        announced.ShouldNotBeNull();
        announced!.Identity.Id.ShouldBe("stage-web-01");
        announced.Identity.PluginId.ShouldBe("stage-web");
        announced.Name.ShouldBe("Seren Web");

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    [Fact]
    public async Task InvalidJson_ShouldReceiveErrorEnvelope()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        using var socket = await client.ConnectAsync(uri, ct);
        _ = await ReceiveEnvelopeAsync(socket, ct); // hello

        var bytes = Encoding.UTF8.GetBytes("{ this is not valid json");

        // act
        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            ct);

        var response = await ReceiveEnvelopeAsync(socket, ct);

        // assert
        response.Type.ShouldBe(EventTypes.Error);
        var error = response.Data.Deserialize(SerenJsonContext.Default.ErrorPayload);
        error.ShouldNotBeNull();
        error!.Message.ShouldContain("JSON");

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    [Fact]
    public async Task HeartbeatPing_ShouldReceivePongEnvelope()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        using var socket = await client.ConnectAsync(uri, ct);
        _ = await ReceiveEnvelopeAsync(socket, ct); // hello

        var ping = new WebSocketEnvelope
        {
            Type = EventTypes.TransportHeartbeat,
            Data = JsonSerializer.SerializeToElement(
                new HeartbeatPayload { Kind = "ping", At = 1 },
                SerenJsonContext.Default.HeartbeatPayload),
            Metadata = new EventMetadata
            {
                Source = new ModuleIdentityDto { Id = "client-1", PluginId = "test" },
                Event = new EventIdentity { Id = "ping-1" },
            },
        };

        // act
        await SendEnvelopeAsync(socket, ping, ct);
        var response = await ReceiveEnvelopeAsync(socket, ct);

        // assert
        response.Type.ShouldBe(EventTypes.TransportHeartbeat);
        var pong = response.Data.Deserialize(SerenJsonContext.Default.HeartbeatPayload);
        pong.ShouldNotBeNull();
        pong!.Kind.ShouldBe("pong");

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    private static async Task SendEnvelopeAsync(
        WebSocket socket,
        WebSocketEnvelope envelope,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(envelope, SerenJsonContext.Default.WebSocketEnvelope);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            ct);
    }

    private static async Task<WebSocketEnvelope> ReceiveEnvelopeAsync(
        WebSocket socket,
        CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Server closed the socket before sending a frame.");
            }

            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        var envelope = JsonSerializer.Deserialize(
            ms.ToArray(),
            SerenJsonContext.Default.WebSocketEnvelope);
        envelope.ShouldNotBeNull();
        return envelope!;
    }
}
