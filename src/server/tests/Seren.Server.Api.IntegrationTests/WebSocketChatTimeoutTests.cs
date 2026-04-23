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
/// Asserts the server-side safety net: when the OpenClaw stream stalls
/// (no chunk for &gt; <c>OpenClaw:Chat:IdleTimeout</c>) the hub broadcasts
/// an <c>error</c> envelope with code <c>stream_idle_timeout</c> followed
/// by <c>output:chat:end</c>, both arriving inside a few hundred ms — so
/// the UI never sits frozen on a hung cloud model.
/// </summary>
public sealed class WebSocketChatTimeoutTests
    : IClassFixture<WebSocketChatTimeoutTests.IdleTimeoutFactory>
{
    private readonly IdleTimeoutFactory _factory;

    public WebSocketChatTimeoutTests(IdleTimeoutFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task IdleStream_BroadcastsStreamIdleTimeoutErrorAndChatEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        var wsClient = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        using var sender = await wsClient.ConnectAsync(uri, ct);
        _ = await ReceiveEnvelopeAsync(sender, ct); // drain transport:hello

        var input = new WebSocketEnvelope
        {
            Type = EventTypes.InputText,
            Data = JsonSerializer.SerializeToElement(
                new TextInputPayload { Text = "Are you there?", ClientMessageId = "msg-stall-1" },
                SerenJsonContext.Default.TextInputPayload),
            Metadata = new EventMetadata
            {
                Source = new ModuleIdentityDto { Id = "test", PluginId = "test" },
                Event = new EventIdentity { Id = "evt-stall-1" },
            },
        };
        await SendEnvelopeAsync(sender, input, ct);

        ErrorPayload? error = null;
        var sawEnd = false;
        // IdleTimeout is set to 200ms by the factory; allow generous wall
        // time so flaky CI doesn't trip us. The test fails only if neither
        // an error nor end ever arrive.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));

        while (!sawEnd)
        {
            var env = await ReceiveEnvelopeAsync(sender, deadline.Token);
            switch (env.Type)
            {
                case EventTypes.Error:
                    error = env.Data.Deserialize(SerenJsonContext.Default.ErrorPayload);
                    break;
                case EventTypes.OutputChatEnd:
                    sawEnd = true;
                    break;
            }
        }

        error.ShouldNotBeNull();
        error!.Code.ShouldBe("stream_idle_timeout");
        sawEnd.ShouldBeTrue();

        await sender.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    private static async Task SendEnvelopeAsync(
        WebSocket socket, WebSocketEnvelope envelope, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(envelope, SerenJsonContext.Default.WebSocketEnvelope);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(
            new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, ct);
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
                throw new InvalidOperationException("Server closed the socket before sending a frame.");
            }
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        var envelope = JsonSerializer.Deserialize(ms.ToArray(), SerenJsonContext.Default.WebSocketEnvelope);
        envelope.ShouldNotBeNull();
        return envelope!;
    }

    public sealed class IdleTimeoutFactory : SerenTestFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // Sub-second timeouts so the test runs in well under a second on
            // both local and CI. The validator floor is 1 s so we override
            // the registered options post-bind via a configure callback.
            builder.UseSetting("OpenClaw:Chat:IdleTimeout", "00:00:01");
            builder.UseSetting("OpenClaw:Chat:TotalTimeout", "00:00:05");

            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(s => s.ServiceType == typeof(IOpenClawChat)).ToList())
                {
                    services.Remove(d);
                }
                services.AddSingleton<IOpenClawChat, HangingChat>();

                foreach (var d in services.Where(s => s.ServiceType == typeof(IOpenClawClient)).ToList())
                {
                    services.Remove(d);
                }
                services.AddSingleton<IOpenClawClient, EmptyModels>();
            });
        }
    }

    /// <summary>
    /// Stub that accepts <c>chat.send</c> but never produces a chunk —
    /// blocks until the handler-side idle CTS cancels it. Mirrors a
    /// cloud provider that hung after acknowledging the request.
    /// </summary>
    private sealed class HangingChat : IOpenClawChat
    {
        public Task PinSessionModelAsync(string sessionKey, string? model, CancellationToken ct)
            => Task.CompletedTask;

        public Task<string> StartAsync(
            string sessionKey, string message, string? agentId, string? idempotencyKey, CancellationToken ct)
            => Task.FromResult(idempotencyKey ?? "hung-run");

        public Task AbortAsync(string sessionKey, string runId, CancellationToken ct)
            => Task.CompletedTask;

        public IAsyncEnumerable<ChatStreamDelta> SubscribeAsync(string runId, CancellationToken ct)
            => Enumerate(ct);

        private static async IAsyncEnumerable<ChatStreamDelta> Enumerate(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Block until the handler's idle CTS cancels us — the
            // OperationCanceledException propagates out of the iterator,
            // which is what the handler's catch-when filter relies on
            // to broadcast the timeout error envelope.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            yield break;
        }
    }

    private sealed class EmptyModels : IOpenClawClient
    {
        public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task RefreshCatalogAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
