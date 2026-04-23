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
/// Regression test for the « receive loop bloquée par le stream » bug:
/// a long-running <c>input:text</c> handler must NOT prevent subsequent
/// frames (heartbeats, commands) from being processed on the same WS.
/// The stub holds the stream open via a <see cref="TaskCompletionSource"/>
/// so we can inject a heartbeat mid-flight and verify it is acked before
/// the stream completes.
/// </summary>
public sealed class WebSocketHeartbeatDuringStreamTests
    : IClassFixture<WebSocketHeartbeatDuringStreamTests.GatedStubFactory>
{
    private readonly GatedStubFactory _factory;

    public WebSocketHeartbeatDuringStreamTests(GatedStubFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Heartbeat_ShouldBeAckedWhileChatStreamIsInProgress()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.ResetGate();

        var wsClient = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");
        using var socket = await wsClient.ConnectAsync(uri, ct);

        // drain hello
        _ = await ReceiveEnvelopeAsync(socket, ct);

        // 1) Kick off a chat stream that will stall waiting on the gate.
        await SendEnvelopeAsync(socket,
            new WebSocketEnvelope
            {
                Type = EventTypes.InputText,
                Data = JsonSerializer.SerializeToElement(
                    new TextInputPayload { Text = "Please wait while I think…" },
                    SerenJsonContext.Default.TextInputPayload),
                Metadata = new EventMetadata
                {
                    Source = new ModuleIdentityDto { Id = "test-htbeat", PluginId = "test" },
                    Event = new EventIdentity { Id = "evt-text" },
                },
            }, ct);

        // Wait until the stub has been entered (proves the handler is in
        // flight and still holding the gate).
        await _factory.WaitForStreamStartedAsync(ct);

        // 2) Send a ping. If the receive loop is blocked by the in-flight
        // stream, this frame will never be read and we'll timeout below.
        await SendEnvelopeAsync(socket,
            new WebSocketEnvelope
            {
                Type = EventTypes.TransportHeartbeat,
                Data = JsonSerializer.SerializeToElement(
                    new HeartbeatPayload { Kind = "ping", At = 0 },
                    SerenJsonContext.Default.HeartbeatPayload),
                Metadata = new EventMetadata
                {
                    Source = new ModuleIdentityDto { Id = "test-htbeat", PluginId = "test" },
                    Event = new EventIdentity { Id = "evt-ping" },
                },
            }, ct);

        // 3) Expect a pong back within the test cancellation window.
        WebSocketEnvelope? pong = null;
        while (pong is null)
        {
            var env = await ReceiveEnvelopeAsync(socket, ct);
            if (env.Type == EventTypes.TransportHeartbeat)
            {
                var payload = env.Data.Deserialize(SerenJsonContext.Default.HeartbeatPayload);
                if (payload?.Kind == "pong")
                {
                    pong = env;
                }
            }
        }

        pong.ShouldNotBeNull();

        // 4) Release the stream so the test can exit cleanly. Drain the
        // remaining chat chunks + end.
        _factory.ReleaseStream();
        var sawEnd = false;
        while (!sawEnd)
        {
            var env = await ReceiveEnvelopeAsync(socket, ct);
            if (env.Type == EventTypes.OutputChatEnd)
            {
                sawEnd = true;
            }
        }

        sawEnd.ShouldBeTrue();
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    private static async Task SendEnvelopeAsync(
        WebSocket socket, WebSocketEnvelope envelope, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(envelope, SerenJsonContext.Default.WebSocketEnvelope);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static async Task<WebSocketEnvelope> ReceiveEnvelopeAsync(
        WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Server closed socket unexpectedly.");
            }
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        var env = JsonSerializer.Deserialize(ms.ToArray(), SerenJsonContext.Default.WebSocketEnvelope);
        env.ShouldNotBeNull();
        return env!;
    }

    public sealed class GatedStubFactory : SerenTestFactory
    {
        private TaskCompletionSource _streamStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _releaseGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ResetGate()
        {
            _streamStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForStreamStartedAsync(CancellationToken ct) =>
            _streamStarted.Task.WaitAsync(ct);

        public void ReleaseStream() => _releaseGate.TrySetResult();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                ReplaceSingleton(services, typeof(IOpenClawChat), new GatedChat(this));
                ReplaceSingleton(services, typeof(IOpenClawClient), new EmptyModels());
            });
        }

        private static void ReplaceSingleton(IServiceCollection services, Type serviceType, object instance)
        {
            var existing = services.Where(d => d.ServiceType == serviceType).ToList();
            foreach (var d in existing)
            {
                services.Remove(d);
            }
            services.AddSingleton(serviceType, instance);
        }

        private sealed class GatedChat(GatedStubFactory parent) : IOpenClawChat
        {
            public Task PinSessionModelAsync(string sessionKey, string? model, CancellationToken ct) => Task.CompletedTask;

            public Task<string> StartAsync(
                string sessionKey, string message, string? agentId, string? idempotencyKey, CancellationToken ct)
                => Task.FromResult(idempotencyKey ?? "gated-run");

            public Task AbortAsync(string sessionKey, string runId, CancellationToken ct) => Task.CompletedTask;

            public IAsyncEnumerable<ChatStreamDelta> SubscribeAsync(string runId, CancellationToken ct)
                => Enumerate(ct);

            private async IAsyncEnumerable<ChatStreamDelta> Enumerate([EnumeratorCancellation] CancellationToken ct = default)
            {
                parent._streamStarted.TrySetResult();
                // Wait for the test to release us. While we're waiting here,
                // the session processor's receive loop MUST stay responsive
                // to incoming heartbeats — that's what this test verifies.
                await parent._releaseGate.Task.WaitAsync(ct).ConfigureAwait(false);
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
}
