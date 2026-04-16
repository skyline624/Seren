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
/// End-to-end test: sends an <c>input:text</c> event through the WebSocket pipeline
/// and verifies that <c>output:chat:chunk</c> and <c>output:chat:end</c> envelopes
/// are received back. Uses a stubbed <see cref="IOpenClawClient"/> to avoid
/// external dependencies.
/// </summary>
public sealed class WebSocketTextInputTests : IClassFixture<WebSocketTextInputTests.StubOpenClawFactory>
{
    private readonly StubOpenClawFactory _factory;

    public WebSocketTextInputTests(StubOpenClawFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InputText_ShouldReceiveChatChunksAndChatEnd()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        using var socket = await client.ConnectAsync(uri, ct);

        // drain the hello frame
        _ = await ReceiveEnvelopeAsync(socket, ct);

        var textInput = new WebSocketEnvelope
        {
            Type = EventTypes.InputText,
            Data = JsonSerializer.SerializeToElement(
                new TextInputPayload { Text = "Hello, who are you?" },
                SerenJsonContext.Default.TextInputPayload),
            Metadata = new EventMetadata
            {
                Source = new ModuleIdentityDto
                {
                    Id = "test-client-01",
                    PluginId = "test-client",
                },
                Event = new EventIdentity { Id = "evt-text-1" },
            },
        };

        // act
        await SendEnvelopeAsync(socket, textInput, ct);

        // assert — expect chat chunk(s) then a chat end
        var received = new List<WebSocketEnvelope>();
        while (true)
        {
            var envelope = await ReceiveEnvelopeAsync(socket, ct);
            received.Add(envelope);

            // Fail fast if the server reports an error instead of chat output
            if (envelope.Type == EventTypes.Error)
            {
                var err = envelope.Data.Deserialize(SerenJsonContext.Default.ErrorPayload);
                err.ShouldNotBeNull();
                Assert.Fail($"Server returned error instead of chat output: {err!.Message}");
            }

            if (envelope.Type == EventTypes.OutputChatEnd)
            {
                break;
            }
        }

        // At least one chat chunk + the end
        received.Count.ShouldBeGreaterThanOrEqualTo(2);

        var chunks = received.Where(e => e.Type == EventTypes.OutputChatChunk).ToList();
        chunks.Count.ShouldBeGreaterThanOrEqualTo(1);

        var firstChunk = chunks[0].Data.Deserialize(SerenJsonContext.Default.ChatChunkPayload);
        firstChunk.ShouldNotBeNull();
        firstChunk!.Content.ShouldNotBeNullOrWhiteSpace();

        var end = received.Last();
        end.Type.ShouldBe(EventTypes.OutputChatEnd);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    [Fact]
    public async Task InputText_WithEmptyText_ShouldReceiveValidationError()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        using var socket = await client.ConnectAsync(uri, ct);
        _ = await ReceiveEnvelopeAsync(socket, ct); // hello

        var textInput = new WebSocketEnvelope
        {
            Type = EventTypes.InputText,
            Data = JsonSerializer.SerializeToElement(
                new TextInputPayload { Text = string.Empty },
                SerenJsonContext.Default.TextInputPayload),
            Metadata = new EventMetadata
            {
                Source = new ModuleIdentityDto
                {
                    Id = "test-client-01",
                    PluginId = "test-client",
                },
                Event = new EventIdentity { Id = "evt-text-2" },
            },
        };

        // act
        await SendEnvelopeAsync(socket, textInput, ct);
        var response = await ReceiveEnvelopeAsync(socket, ct);

        // assert — validation should reject empty text
        response.Type.ShouldBe(EventTypes.Error);

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

    /// <summary>
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> that replaces
    /// <see cref="IOpenClawClient"/> with a deterministic stub producing
    /// fixed chat chunks, enabling end-to-end testing without OpenClaw Gateway.
    /// </summary>
    public sealed class StubOpenClawFactory : SerenTestFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                // Replace OpenClaw client with stub
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(IOpenClawClient))
                    .ToList();
                foreach (var d in descriptors)
                {
                    services.Remove(d);
                }

                services.AddSingleton<IOpenClawClient>(new StubOpenClawClient());
            });
        }
    }

    private sealed class StubOpenClawClient : IOpenClawClient
    {
        public IAsyncEnumerable<ChatCompletionChunk> StreamChatAsync(
            IReadOnlyList<ChatMessage> messages,
            string? agentId = null,
            string? sessionKey = null,
            CancellationToken ct = default)
        {
            return EnumerateAsync(ct);
        }

        public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        }

        private static async IAsyncEnumerable<ChatCompletionChunk> EnumerateAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatCompletionChunk("I am Seren, your AI assistant.", null);

            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatCompletionChunk(null, "stop");
        }
    }
}
