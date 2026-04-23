using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Abstractions;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Contracts.Json;
using Shouldly;
using Xunit;

namespace Seren.Server.Api.IntegrationTests;

/// <summary>
/// Exercises the user-turn echo: when peer A sends <c>input:text</c>,
/// peer B must receive an <c>output:chat:user</c> envelope with the
/// sender's <c>ClientMessageId</c> and the original text, while peer A
/// must not (sender is excluded by <c>BroadcastAsync</c>).
/// </summary>
public sealed class WebSocketUserEchoTests : IClassFixture<WebSocketUserEchoTests.StubOpenClawFactory>
{
    private readonly StubOpenClawFactory _factory;

    public WebSocketUserEchoTests(StubOpenClawFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InputText_BroadcastsUserEcho_ToOtherPeersButNotSender()
    {
        var ct = TestContext.Current.CancellationToken;
        var wsClient = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        using var sender = await wsClient.ConnectAsync(uri, ct);
        using var peerB = await wsClient.ConnectAsync(uri, ct);

        // Drain each peer's `transport:hello` so subsequent reads see real events.
        _ = await ReceiveEnvelopeAsync(sender, ct);
        _ = await ReceiveEnvelopeAsync(peerB, ct);

        const string clientMessageId = "msg-echo-test-0001";
        const string text = "Salut peer B";
        var textInput = new WebSocketEnvelope
        {
            Type = EventTypes.InputText,
            Data = JsonSerializer.SerializeToElement(
                new TextInputPayload
                {
                    Text = text,
                    ClientMessageId = clientMessageId,
                },
                SerenJsonContext.Default.TextInputPayload),
            Metadata = new EventMetadata
            {
                Source = new ModuleIdentityDto
                {
                    Id = "peer-A",
                    PluginId = "test-client",
                },
                Event = new EventIdentity { Id = "evt-echo-1" },
            },
        };

        await SendEnvelopeAsync(sender, textInput, ct);

        // Peer B's first live frame must be the user-turn echo (arrives
        // before any output:chat:chunk because the handler broadcasts it
        // immediately after command dispatch and the stub chat takes a
        // full async tick to yield its first delta).
        UserEchoPayload? echo = null;
        while (true)
        {
            var env = await ReceiveEnvelopeAsync(peerB, ct);
            if (env.Type == EventTypes.OutputChatUser)
            {
                echo = env.Data.Deserialize(SerenJsonContext.Default.UserEchoPayload);
                break;
            }

            // Any error / end short-circuits with a clear failure.
            if (env.Type == EventTypes.Error)
            {
                var err = env.Data.Deserialize(SerenJsonContext.Default.ErrorPayload);
                Assert.Fail($"Peer B received an error instead of echo: {err?.Message}");
            }

            if (env.Type == EventTypes.OutputChatEnd)
            {
                Assert.Fail("Peer B reached chat:end without ever seeing output:chat:user.");
            }
        }

        echo.ShouldNotBeNull();
        echo!.MessageId.ShouldBe(clientMessageId);
        echo.Text.ShouldBe(text);
        echo.TimestampMs.ShouldBeGreaterThan(0);

        // Sender must NOT see its own echo — drain its stream up to chat:end
        // and confirm no output:chat:user slipped through.
        var senderFrames = new List<WebSocketEnvelope>();
        while (true)
        {
            var env = await ReceiveEnvelopeAsync(sender, ct);
            senderFrames.Add(env);
            if (env.Type == EventTypes.OutputChatEnd)
            {
                break;
            }
        }

        senderFrames
            .Where(e => e.Type == EventTypes.OutputChatUser)
            .ShouldBeEmpty("the sender must be excluded from its own echo broadcast");

        await sender.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
        await peerB.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    [Fact]
    public async Task InputText_WithoutClientMessageId_StillEchoesWithServerMintedId()
    {
        var ct = TestContext.Current.CancellationToken;
        var wsClient = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        using var sender = await wsClient.ConnectAsync(uri, ct);
        using var peerB = await wsClient.ConnectAsync(uri, ct);

        _ = await ReceiveEnvelopeAsync(sender, ct);
        _ = await ReceiveEnvelopeAsync(peerB, ct);

        var textInput = new WebSocketEnvelope
        {
            Type = EventTypes.InputText,
            Data = JsonSerializer.SerializeToElement(
                new TextInputPayload { Text = "No id here" },
                SerenJsonContext.Default.TextInputPayload),
            Metadata = new EventMetadata
            {
                Source = new ModuleIdentityDto { Id = "peer-A", PluginId = "test-client" },
                Event = new EventIdentity { Id = "evt-echo-2" },
            },
        };

        await SendEnvelopeAsync(sender, textInput, ct);

        while (true)
        {
            var env = await ReceiveEnvelopeAsync(peerB, ct);
            if (env.Type == EventTypes.OutputChatUser)
            {
                var echo = env.Data.Deserialize(SerenJsonContext.Default.UserEchoPayload);
                echo.ShouldNotBeNull();
                echo!.MessageId.ShouldNotBeNullOrWhiteSpace();
                echo.Text.ShouldBe("No id here");
                break;
            }
            if (env.Type == EventTypes.OutputChatEnd)
            {
                Assert.Fail("Peer B reached chat:end without seeing output:chat:user.");
            }
        }

        await sender.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
        await peerB.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
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

        var envelope = JsonSerializer.Deserialize(ms.ToArray(), SerenJsonContext.Default.WebSocketEnvelope);
        envelope.ShouldNotBeNull();
        return envelope!;
    }

    public sealed class StubOpenClawFactory : SerenTestFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(s => s.ServiceType == typeof(IOpenClawChat)).ToList())
                {
                    services.Remove(d);
                }
                services.AddSingleton<IOpenClawChat, SlowStubChat>();

                foreach (var d in services.Where(s => s.ServiceType == typeof(IOpenClawClient)).ToList())
                {
                    services.Remove(d);
                }
                services.AddSingleton<IOpenClawClient, EmptyModels>();
            });
        }
    }

    /// <summary>
    /// Chat stub that delays the first chunk so the echo broadcast
    /// (issued synchronously right after <c>chat.send</c> resolves)
    /// reliably reaches peer B before the first assistant token.
    /// </summary>
    private sealed class SlowStubChat : IOpenClawChat
    {
        public Task PinSessionModelAsync(string sessionKey, string? model, CancellationToken ct)
            => Task.CompletedTask;

        public Task<string> StartAsync(
            string sessionKey, string message, string? agentId, string? idempotencyKey, CancellationToken ct)
            => Task.FromResult(idempotencyKey ?? "echo-test-run");

        public Task AbortAsync(string sessionKey, string runId, CancellationToken ct) => Task.CompletedTask;

        public IAsyncEnumerable<ChatStreamDelta> SubscribeAsync(string runId, CancellationToken ct)
            => Enumerate(ct);

        private static async IAsyncEnumerable<ChatStreamDelta> Enumerate(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // Small delay so the echo is guaranteed to land on peer B
            // before the first chunk. In prod the assistant stream is
            // typically 50-200ms slower than the broadcast anyway.
            await Task.Delay(50, ct);
            yield return new ChatStreamDelta("ok", null);
            yield return new ChatStreamDelta(null, "stop");
        }
    }

    private sealed class EmptyModels : IOpenClawClient
    {
        public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task RefreshCatalogAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
