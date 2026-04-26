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
/// Verifies the user-Stop path: peer sends <c>input:text</c>, server starts
/// the run, peer sends <c>input:chat:abort</c>, the chat client's
/// <c>AbortAsync</c> receives the right runId, and the stream closes
/// cleanly with <c>output:chat:end</c>.
/// </summary>
public sealed class WebSocketChatAbortTests
    : IClassFixture<WebSocketChatAbortTests.AbortableChatFactory>
{
    private readonly AbortableChatFactory _factory;

    public WebSocketChatAbortTests(AbortableChatFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InputChatAbort_AbortsTheActiveRunAndEndsTheStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var wsClient = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        using var sender = await wsClient.ConnectAsync(uri, ct);
        _ = await ReceiveEnvelopeAsync(sender, ct); // drain transport:hello

        const string clientMessageId = "msg-abort-test-1";

        var input = new WebSocketEnvelope
        {
            Type = EventTypes.InputText,
            Data = JsonSerializer.SerializeToElement(
                new TextInputPayload { Text = "Long answer please", ClientMessageId = clientMessageId },
                SerenJsonContext.Default.TextInputPayload),
            Metadata = new EventMetadata
            {
                Source = new ModuleIdentityDto { Id = "test", PluginId = "test" },
                Event = new EventIdentity { Id = "evt-abort-1" },
            },
        };
        await SendEnvelopeAsync(sender, input, ct);

        // Wait until the stub is actually running, otherwise the abort
        // would target an unregistered run and silently no-op.
        await _factory.Chat.RunStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        var abort = new WebSocketEnvelope
        {
            Type = EventTypes.InputChatAbort,
            Data = JsonSerializer.SerializeToElement(
                new ChatAbortPayload { RunId = clientMessageId },
                SerenJsonContext.Default.ChatAbortPayload),
            Metadata = new EventMetadata
            {
                Source = new ModuleIdentityDto { Id = "test", PluginId = "test" },
                Event = new EventIdentity { Id = "evt-abort-2" },
            },
        };
        await SendEnvelopeAsync(sender, abort, ct);

        // The stub releases its stream when AbortAsync fires, after which
        // the handler's finally block emits OutputChatEnd.
        var sawEnd = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (!sawEnd)
        {
            var env = await ReceiveEnvelopeAsync(sender, deadline.Token);
            if (env.Type == EventTypes.OutputChatEnd)
            {
                sawEnd = true;
            }
        }

        sawEnd.ShouldBeTrue();
        _factory.Chat.AbortedRunId.ShouldBe(clientMessageId);

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

    public sealed class AbortableChatFactory : SerenTestFactory
    {
        public AbortableChat Chat { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(s => s.ServiceType == typeof(IOpenClawChat)).ToList())
                {
                    services.Remove(d);
                }
                services.AddSingleton<IOpenClawChat>(Chat);

                foreach (var d in services.Where(s => s.ServiceType == typeof(IOpenClawClient)).ToList())
                {
                    services.Remove(d);
                }
                services.AddSingleton<IOpenClawClient, EmptyModels>();
            });
        }
    }

    /// <summary>
    /// Stub whose stream blocks until <c>AbortAsync</c> fires, simulating a
    /// long-running model that the user cuts short.
    /// </summary>
    public sealed class AbortableChat : IOpenClawChat
    {
        public TaskCompletionSource RunStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _abortSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? AbortedRunId { get; private set; }

        public Task PinSessionModelAsync(string sessionKey, string? model, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<string> StartAsync(
            string sessionKey, string message, string? agentId, string? idempotencyKey,
            IReadOnlyList<ChatImageAttachment>? imageAttachments, CancellationToken cancellationToken)
            => Task.FromResult(idempotencyKey ?? "abort-test-run");

        public Task AbortAsync(string sessionKey, string runId, CancellationToken cancellationToken)
        {
            AbortedRunId = runId;
            _abortSignal.TrySetResult();
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<ChatStreamDelta> SubscribeAsync(string runId, CancellationToken cancellationToken)
            => Enumerate(cancellationToken);

        private async IAsyncEnumerable<ChatStreamDelta> Enumerate(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RunStarted.TrySetResult();
            // Block until either AbortAsync fires or the stream gets cancelled
            // (idle/total/peer disconnect). On abort we yield a clean Final.
            await Task.WhenAny(_abortSignal.Task, Task.Delay(Timeout.Infinite, cancellationToken))
                .ConfigureAwait(false);

            if (_abortSignal.Task.IsCompleted)
            {
                yield return new ChatStreamDelta(null, "abort");
            }
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
