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
/// End-to-end test of the fallback cascade: the primary model and all
/// its retries hang; the configured fallback model then takes over and
/// completes the stream. The UI sees degraded notices for each transition
/// and eventually receives content + a clean end — no error.
/// </summary>
public sealed class WebSocketChatFallbackTests
    : IClassFixture<WebSocketChatFallbackTests.FallbackFactory>
{
    private readonly FallbackFactory _factory;

    public WebSocketChatFallbackTests(FallbackFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PrimaryExhausted_FallbackModelUsed_AndStreamsContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var wsClient = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        using var sender = await wsClient.ConnectAsync(uri, ct);
        _ = await ReceiveEnvelopeAsync(sender, ct); // transport:hello

        var input = new WebSocketEnvelope
        {
            Type = EventTypes.InputText,
            Data = JsonSerializer.SerializeToElement(
                new TextInputPayload { Text = "Hi", ClientMessageId = "msg-fallback-1", Model = "provider/primary-model" },
                SerenJsonContext.Default.TextInputPayload),
            Metadata = new EventMetadata
            {
                Source = new ModuleIdentityDto { Id = "test", PluginId = "test" },
                Event = new EventIdentity { Id = "evt-fallback-1" },
            },
        };
        await SendEnvelopeAsync(sender, input, ct);

        var degradedCount = 0;
        var sawChunk = false;
        var sawError = false;
        var sawEnd = false;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));

        while (!sawEnd)
        {
            var env = await ReceiveEnvelopeAsync(sender, deadline.Token);
            switch (env.Type)
            {
                case EventTypes.OutputChatProviderDegraded:
                    degradedCount++;
                    break;
                case EventTypes.OutputChatChunk:
                    sawChunk = true;
                    break;
                case EventTypes.Error:
                    sawError = true;
                    break;
                case EventTypes.OutputChatEnd:
                    sawEnd = true;
                    break;
            }
        }

        // One retry on primary + one cascade to fallback = 2 degraded notices.
        degradedCount.ShouldBe(2);
        sawChunk.ShouldBeTrue();
        sawError.ShouldBeFalse();
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

    public sealed class FallbackFactory : SerenTestFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseSetting("OpenClaw:Chat:IdleTimeout", "00:00:01");
            builder.UseSetting("OpenClaw:Chat:TotalTimeout", "00:00:15");
            builder.UseSetting("OpenClaw:Chat:Resilience:RetryOnIdleBeforeFirstChunk", "1");
            builder.UseSetting("OpenClaw:Chat:Resilience:RetryBackoff", "00:00:00.050");
            builder.UseSetting("OpenClaw:Chat:Resilience:FallbackModels:0", "provider/fallback-model");

            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(s => s.ServiceType == typeof(IOpenClawChat)).ToList())
                {
                    services.Remove(d);
                }
                services.AddSingleton<IOpenClawChat, HangOnPrimaryChat>();

                foreach (var d in services.Where(s => s.ServiceType == typeof(IOpenClawClient)).ToList())
                {
                    services.Remove(d);
                }
                services.AddSingleton<IOpenClawClient, EmptyModels>();
            });
        }
    }

    /// <summary>
    /// Hangs whenever called with the primary model; streams a short OK
    /// response for any other model. Captures the per-call model so the
    /// test can assert the cascade took the right path.
    /// </summary>
    private sealed class HangOnPrimaryChat : IOpenClawChat
    {
        private const string Primary = "provider/primary-model";
        private int _counter;
        public List<string?> ModelsTried { get; } = [];

        public Task PinSessionModelAsync(string sessionKey, string? model, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<string> StartAsync(
            string sessionKey, string message, string? agentId, string? idempotencyKey,
            IReadOnlyList<ChatImageAttachment>? imageAttachments, CancellationToken cancellationToken)
        {
            ModelsTried.Add(agentId);
            var n = Interlocked.Increment(ref _counter);
            // Encode the model choice into the runId so SubscribeAsync knows
            // whether to hang or stream.
            return Task.FromResult(agentId == Primary ? $"primary-{n}" : $"fallback-{n}");
        }

        public Task AbortAsync(string sessionKey, string runId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public IAsyncEnumerable<ChatStreamDelta> SubscribeAsync(string runId, CancellationToken cancellationToken)
            => runId.StartsWith("primary-", StringComparison.Ordinal)
                ? HangForever(cancellationToken)
                : StreamOk(cancellationToken);

        private static async IAsyncEnumerable<ChatStreamDelta> HangForever(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            yield break;
        }

        private static async IAsyncEnumerable<ChatStreamDelta> StreamOk(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatStreamDelta("From fallback!", null);
            yield return new ChatStreamDelta(null, "stop");
        }
    }

    private sealed class EmptyModels : IOpenClawClient
    {
        public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task RefreshCatalogAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetDefaultModelAsync(string? model, CancellationToken ct = default) => Task.CompletedTask;
    }
}
