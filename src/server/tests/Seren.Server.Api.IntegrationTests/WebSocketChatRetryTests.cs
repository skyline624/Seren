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
/// End-to-end test of the retry-on-idle-before-first-chunk path: the
/// primary provider hangs on the first attempt, the pipeline auto-retries
/// the same model, and the second attempt yields a clean stream. The UI
/// sees one <c>output:chat:provider-degraded</c> info notice, the normal
/// content chunks, and a clean <c>output:chat:end</c> — no error.
/// </summary>
public sealed class WebSocketChatRetryTests
    : IClassFixture<WebSocketChatRetryTests.RetryingChatFactory>
{
    private readonly RetryingChatFactory _factory;

    public WebSocketChatRetryTests(RetryingChatFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task IdleOnFirstAttempt_RetriesTransparently_AndStreamsContent()
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
                new TextInputPayload { Text = "Hi", ClientMessageId = "msg-retry-1" },
                SerenJsonContext.Default.TextInputPayload),
            Metadata = new EventMetadata
            {
                Source = new ModuleIdentityDto { Id = "test", PluginId = "test" },
                Event = new EventIdentity { Id = "evt-retry-1" },
            },
        };
        await SendEnvelopeAsync(sender, input, ct);

        var sawDegraded = false;
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
                    sawDegraded = true;
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

        sawDegraded.ShouldBeTrue("UI should have seen the transparent-retry notice");
        sawChunk.ShouldBeTrue("retried attempt should have streamed content");
        sawError.ShouldBeFalse("successful retry must not surface an error to the UI");
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

    public sealed class RetryingChatFactory : SerenTestFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // Sub-second idle horizon so the first-attempt hang fires quickly.
            builder.UseSetting("OpenClaw:Chat:IdleTimeout", "00:00:01");
            builder.UseSetting("OpenClaw:Chat:TotalTimeout", "00:00:10");
            builder.UseSetting("OpenClaw:Chat:Resilience:RetryOnIdleBeforeFirstChunk", "1");
            builder.UseSetting("OpenClaw:Chat:Resilience:RetryBackoff", "00:00:00.050");

            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(s => s.ServiceType == typeof(IOpenClawChat)).ToList())
                {
                    services.Remove(d);
                }
                services.AddSingleton<IOpenClawChat, RetryAfterHangChat>();

                foreach (var d in services.Where(s => s.ServiceType == typeof(IOpenClawClient)).ToList())
                {
                    services.Remove(d);
                }
                services.AddSingleton<IOpenClawClient, EmptyModels>();
            });
        }
    }

    /// <summary>
    /// First <c>StartAsync</c> call produces a run that hangs forever; the
    /// second produces a normal short stream. Models the real-world "cloud
    /// endpoint occasionally doesn't emit anything" failure mode.
    /// </summary>
    private sealed class RetryAfterHangChat : IOpenClawChat
    {
        private int _attempts;

        public Task PinSessionModelAsync(string sessionKey, string? model, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<string> StartAsync(
            string sessionKey, string message, string? agentId, string? idempotencyKey, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _attempts);
            return Task.FromResult($"run-{n}");
        }

        public Task AbortAsync(string sessionKey, string runId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public IAsyncEnumerable<ChatStreamDelta> SubscribeAsync(string runId, CancellationToken cancellationToken)
        {
            // runId tells us which attempt we are on.
            return runId == "run-1" ? HangForever(cancellationToken) : StreamOk(cancellationToken);
        }

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
            yield return new ChatStreamDelta("Recovered!", null);
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
