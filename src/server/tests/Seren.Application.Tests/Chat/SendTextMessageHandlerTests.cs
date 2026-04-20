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
    // Envelope payloads are serialized camelCase on the wire, so tests must
    // deserialize with the same naming policy.
    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Handle_ForwardsUserText_AsChatSendMessage()
    {
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

        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("Hello!", null), new ChatStreamDelta(null, "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();

        var handler = new SendTextMessageHandler(
            chat, repository, hub, SessionKeyProvider, NullLogger<SendTextMessageHandler>.Instance);

        await handler.Handle(new SendTextMessageCommand("Hi there"), ct);

        chat.CapturedMessage.ShouldBe("Hi there");
        chat.CapturedSessionKey.ShouldBe(TestSessionKey);
        chat.CapturedAgentId.ShouldBe("agent-1");
    }

    [Fact]
    public async Task Handle_WithoutActiveCharacter_ForwardsUserTextWithNullAgent()
    {
        var ct = TestContext.Current.CancellationToken;

        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("Hi", null)));
        var repository = new FakeCharacterRepository(null);
        var hub = new FakeSerenHub();

        var handler = new SendTextMessageHandler(
            chat, repository, hub, SessionKeyProvider, NullLogger<SendTextMessageHandler>.Instance);

        await handler.Handle(new SendTextMessageCommand("Hello"), ct);

        chat.CapturedMessage.ShouldBe("Hello");
        chat.CapturedAgentId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WithEmotionMarkers_BroadcastsChunkAndEmotion()
    {
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

        var chat = new FakeOpenClawChat(Streams(
            new ChatStreamDelta("I am so <emotion:joy>happy to see you!", null)));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();

        var handler = new SendTextMessageHandler(
            chat, repository, hub, SessionKeyProvider, NullLogger<SendTextMessageHandler>.Instance);

        await handler.Handle(new SendTextMessageCommand("Hi"), ct);

        hub.BroadcastEnvelopes.Count.ShouldBe(3);

        var chunkEnvelope = hub.BroadcastEnvelopes.FirstOrDefault(e => e.Type == EventTypes.OutputChatChunk);
        chunkEnvelope.ShouldNotBeNull();
        var chunkPayload = JsonSerializer.Deserialize<ChatChunkPayload>(
            chunkEnvelope.Data.GetRawText(), CamelCaseJson);
        chunkPayload!.Content.ShouldContain("happy to see you!");
        chunkPayload.Content.ShouldNotContain("<emotion:joy>");

        var emotionEnvelope = hub.BroadcastEnvelopes.FirstOrDefault(e => e.Type == EventTypes.AvatarEmotion);
        emotionEnvelope.ShouldNotBeNull();
        var emotionPayload = JsonSerializer.Deserialize<AvatarEmotionPayload>(
            emotionEnvelope.Data.GetRawText(), CamelCaseJson);
        emotionPayload!.Emotion.ShouldBe("joy");

        hub.BroadcastEnvelopes.ShouldContain(e => e.Type == EventTypes.OutputChatEnd);
    }

    [Fact]
    public async Task Handle_WithActionMarker_BroadcastsAvatarAction()
    {
        var ct = TestContext.Current.CancellationToken;

        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("<action:wave>Hi there!", null)));
        var repository = new FakeCharacterRepository(null);
        var hub = new FakeSerenHub();
        var handler = new SendTextMessageHandler(
            chat, repository, hub, SessionKeyProvider, NullLogger<SendTextMessageHandler>.Instance);

        await handler.Handle(new SendTextMessageCommand("Hi"), ct);

        var actionEnvelopes = hub.BroadcastEnvelopes
            .Where(e => e.Type == EventTypes.AvatarAction)
            .ToList();
        actionEnvelopes.Count.ShouldBe(1);
        var actionPayload = JsonSerializer.Deserialize<AvatarActionPayload>(
            actionEnvelopes[0].Data.GetRawText(), CamelCaseJson);
        actionPayload!.Action.ShouldBe("wave");

        var chunkEnvelope = hub.BroadcastEnvelopes.First(e => e.Type == EventTypes.OutputChatChunk);
        var chunkPayload = JsonSerializer.Deserialize<ChatChunkPayload>(
            chunkEnvelope.Data.GetRawText(), CamelCaseJson);
        chunkPayload!.Content.ShouldNotContain("<action:wave>");
        chunkPayload.Content.ShouldContain("Hi there!");
    }

    [Fact]
    public async Task Handle_WithExplicitModelOverride_PassesThatModelToGateway()
    {
        var ct = TestContext.Current.CancellationToken;
        var character = new Character(
            Id: Guid.NewGuid(),
            Name: "Seren",
            SystemPrompt: "You are helpful.",
            VrmAssetPath: null,
            Voice: null,
            AgentId: "ollama/default",
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("ok", "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();
        var handler = new SendTextMessageHandler(
            chat, repository, hub, SessionKeyProvider, NullLogger<SendTextMessageHandler>.Instance);

        await handler.Handle(
            new SendTextMessageCommand("Hi", Model: "openai/gpt-4o-mini"), ct);

        chat.CapturedAgentId.ShouldBe("openai/gpt-4o-mini");
    }

    [Fact]
    public async Task Handle_WithoutModelOverride_FallsBackToCharacterAgentId()
    {
        var ct = TestContext.Current.CancellationToken;
        var character = new Character(
            Id: Guid.NewGuid(),
            Name: "Seren",
            SystemPrompt: "You are helpful.",
            VrmAssetPath: null,
            Voice: null,
            AgentId: "ollama/default",
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("ok", "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();
        var handler = new SendTextMessageHandler(
            chat, repository, hub, SessionKeyProvider, NullLogger<SendTextMessageHandler>.Instance);

        await handler.Handle(new SendTextMessageCommand("Hi"), ct);

        chat.CapturedAgentId.ShouldBe("ollama/default");
    }

    [Fact]
    public async Task Handle_WhenStreamEnds_BroadcastsChatEnd()
    {
        var ct = TestContext.Current.CancellationToken;

        var chat = new FakeOpenClawChat(Streams(
            new ChatStreamDelta("Hello", null),
            new ChatStreamDelta(" world", null)));
        var repository = new FakeCharacterRepository(null);
        var hub = new FakeSerenHub();

        var handler = new SendTextMessageHandler(
            chat, repository, hub, SessionKeyProvider, NullLogger<SendTextMessageHandler>.Instance);

        await handler.Handle(new SendTextMessageCommand("Hi"), ct);

        hub.BroadcastEnvelopes.Count.ShouldBe(3);
        hub.BroadcastEnvelopes.Count(e => e.Type == EventTypes.OutputChatChunk).ShouldBe(2);
        hub.BroadcastEnvelopes.Count(e => e.Type == EventTypes.OutputChatEnd).ShouldBe(1);
    }

    private static ChatStreamDelta[] Streams(params ChatStreamDelta[] deltas) => deltas;

    private const string TestSessionKey = "seren-test";
    private static readonly IChatSessionKeyProvider SessionKeyProvider = new FakeSessionKeyProvider(TestSessionKey);

    private sealed class FakeSessionKeyProvider(string key) : IChatSessionKeyProvider
    {
        public string MainSessionKey { get; } = key;
        public Task<string> RotateAsync(CancellationToken cancellationToken) => Task.FromResult(MainSessionKey);
    }

    private sealed class FakeOpenClawChat : IOpenClawChat
    {
        private readonly ChatStreamDelta[] _deltas;

        public string? CapturedSessionKey { get; private set; }
        public string? CapturedMessage { get; private set; }
        public string? CapturedAgentId { get; private set; }

        public FakeOpenClawChat(ChatStreamDelta[] deltas)
        {
            _deltas = deltas;
        }

        public Task<string> StartAsync(string sessionKey, string message, string? agentId, CancellationToken cancellationToken)
        {
            CapturedSessionKey = sessionKey;
            CapturedMessage = message;
            CapturedAgentId = agentId;
            return Task.FromResult("run-fake");
        }

        public IAsyncEnumerable<ChatStreamDelta> SubscribeAsync(string runId, CancellationToken cancellationToken)
        {
            return EnumerateAsync(_deltas, cancellationToken);
        }

        private static async IAsyncEnumerable<ChatStreamDelta> EnumerateAsync(
            IReadOnlyList<ChatStreamDelta> deltas,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var delta in deltas)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return delta;
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

        public Task<Character?> GetActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_active);

        public Task<Character?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_active);

        public Task<IReadOnlyList<Character>> GetAllAsync(CancellationToken cancellationToken)
        {
            var list = _active is not null ? [_active] : Array.Empty<Character>();
            return Task.FromResult<IReadOnlyList<Character>>(list);
        }

        public Task AddAsync(Character character, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateAsync(Character character, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task SetActiveAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSerenHub : ISerenHub
    {
        public List<WebSocketEnvelope> BroadcastEnvelopes { get; } = [];

        public Task<bool> SendAsync(PeerId peerId, WebSocketEnvelope envelope, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<int> BroadcastAsync(WebSocketEnvelope envelope, PeerId? excluding, CancellationToken cancellationToken)
        {
            BroadcastEnvelopes.Add(envelope);
            return Task.FromResult(BroadcastEnvelopes.Count);
        }
    }
}
