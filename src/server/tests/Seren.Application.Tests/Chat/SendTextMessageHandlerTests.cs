using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;
using Shouldly;
using Xunit;

using AppICharacterRepository = Seren.Application.Abstractions.ICharacterRepository;

namespace Seren.Application.Tests.Chat;

public sealed class SendTextMessageHandlerTests
{
    [Fact]
    public async Task Handle_WithActiveCharacter_ShouldIncludeSystemPromptInMessages()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;

        var character = new Character(
            Id: Guid.NewGuid(),
            Name: "Airi",
            SystemPrompt: "You are a helpful assistant.",
            VrmAssetPath: null,
            Voice: null,
            AgentId: "agent-1",
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var client = new FakeOpenClawClient([new("Hello!", null), new(null, "stop")]);
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();

        var handler = new SendTextMessageHandler(
            client, repository, hub, NullLogger<SendTextMessageHandler>.Instance);

        var command = new SendTextMessageCommand("Hi there");

        // act
        await handler.Handle(command, ct);

        // assert — StreamChatAsync was called with system prompt + user message
        client.CapturedMessages.ShouldNotBeNull();
        client.CapturedMessages!.Count.ShouldBe(2);
        client.CapturedMessages[0].Role.ShouldBe("system");
        client.CapturedMessages[0].Content.ShouldBe("You are a helpful assistant.");
        client.CapturedMessages[1].Role.ShouldBe("user");
        client.CapturedMessages[1].Content.ShouldBe("Hi there");
        client.CapturedAgentId.ShouldBe("agent-1");
    }

    [Fact]
    public async Task Handle_WithoutActiveCharacter_ShouldCallStreamChatAsyncWithoutSystemPrompt()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;

        var client = new FakeOpenClawClient([new("Hi", null)]);
        var repository = new FakeCharacterRepository(null);
        var hub = new FakeSerenHub();

        var handler = new SendTextMessageHandler(
            client, repository, hub, NullLogger<SendTextMessageHandler>.Instance);

        var command = new SendTextMessageCommand("Hello");

        // act
        await handler.Handle(command, ct);

        // assert — only the user message, no system prompt
        client.CapturedMessages.ShouldNotBeNull();
        client.CapturedMessages!.Count.ShouldBe(1);
        client.CapturedMessages[0].Role.ShouldBe("user");
        client.CapturedMessages[0].Content.ShouldBe("Hello");
        client.CapturedAgentId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WithEmotionMarkers_ShouldBroadcastChatChunkAndAvatarEmotion()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;

        var character = new Character(
            Id: Guid.NewGuid(),
            Name: "Airi",
            SystemPrompt: "You are happy.",
            VrmAssetPath: null,
            Voice: null,
            AgentId: null,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var client = new FakeOpenClawClient([new("I am so <emotion:joy>happy to see you!", null)]);
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();

        var handler = new SendTextMessageHandler(
            client, repository, hub, NullLogger<SendTextMessageHandler>.Instance);

        var command = new SendTextMessageCommand("Hi");

        // act
        await handler.Handle(command, ct);

        // assert — should have: 1 chat chunk + 1 avatar emotion + 1 chat end = 3 broadcasts
        hub.BroadcastEnvelopes.Count.ShouldBe(3);

        var chunkEnvelope = hub.BroadcastEnvelopes.FirstOrDefault(e => e.Type == EventTypes.OutputChatChunk);
        chunkEnvelope.ShouldNotBeNull();
        var chunkPayload = JsonSerializer.Deserialize<ChatChunkPayload>(chunkEnvelope.Data.GetRawText());
        chunkPayload!.Content.ShouldContain("happy to see you!");
        chunkPayload.Content.ShouldNotContain("<emotion:joy>");

        var emotionEnvelope = hub.BroadcastEnvelopes.FirstOrDefault(e => e.Type == EventTypes.AvatarEmotion);
        emotionEnvelope.ShouldNotBeNull();
        var emotionPayload = JsonSerializer.Deserialize<AvatarEmotionPayload>(emotionEnvelope.Data.GetRawText());
        emotionPayload!.Emotion.ShouldBe("joy");

        var endEnvelope = hub.BroadcastEnvelopes.FirstOrDefault(e => e.Type == EventTypes.OutputChatEnd);
        endEnvelope.ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_WhenStreamEnds_ShouldBroadcastChatEnd()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;

        var client = new FakeOpenClawClient([new("Hello", null), new(" world", null)]);
        var repository = new FakeCharacterRepository(null);
        var hub = new FakeSerenHub();

        var handler = new SendTextMessageHandler(
            client, repository, hub, NullLogger<SendTextMessageHandler>.Instance);

        var command = new SendTextMessageCommand("Hi");

        // act
        await handler.Handle(command, ct);

        // assert — 2 chat chunks + 1 chat end = 3 total
        hub.BroadcastEnvelopes.Count.ShouldBe(3);

        var chatChunks = hub.BroadcastEnvelopes.Where(e => e.Type == EventTypes.OutputChatChunk).ToList();
        chatChunks.Count.ShouldBe(2);

        var endEnvelopes = hub.BroadcastEnvelopes.Where(e => e.Type == EventTypes.OutputChatEnd).ToList();
        endEnvelopes.Count.ShouldBe(1);
    }

    private sealed class FakeOpenClawClient : IOpenClawClient
    {
        private readonly List<ChatCompletionChunk> _chunks;

        public IReadOnlyList<ChatMessage>? CapturedMessages { get; private set; }
        public string? CapturedAgentId { get; private set; }

        public FakeOpenClawClient(List<ChatCompletionChunk> chunks)
        {
            _chunks = chunks;
        }

        public IAsyncEnumerable<ChatCompletionChunk> StreamChatAsync(
            IReadOnlyList<ChatMessage> messages,
            string? agentId = null,
            CancellationToken ct = default)
        {
            CapturedMessages = messages;
            CapturedAgentId = agentId;
            return EnumerateAsync(_chunks, ct);
        }

        public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        }

        private static async IAsyncEnumerable<ChatCompletionChunk> EnumerateAsync(
            IReadOnlyList<ChatCompletionChunk> chunks,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
        }
    }

    private sealed class FakeCharacterRepository : AppICharacterRepository
    {
        private readonly Character? _active;

        public FakeCharacterRepository(Character? active)
        {
            _active = active;
        }

        public Task<Character?> GetActiveAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_active);
        }

        public Task<Character?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(_active);
        }

        public Task<IReadOnlyList<Character>> GetAllAsync(CancellationToken cancellationToken)
        {
            var list = _active is not null ? [_active] : Array.Empty<Character>();
            return Task.FromResult<IReadOnlyList<Character>>(list);
        }

        public Task AddAsync(Character character, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Character character, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSerenHub : ISerenHub
    {
        public List<WebSocketEnvelope> BroadcastEnvelopes { get; } = [];

        public Task<bool> SendAsync(PeerId peerId, WebSocketEnvelope envelope, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<int> BroadcastAsync(WebSocketEnvelope envelope, PeerId? excluding, CancellationToken cancellationToken)
        {
            BroadcastEnvelopes.Add(envelope);
            return Task.FromResult(BroadcastEnvelopes.Count);
        }
    }
}
