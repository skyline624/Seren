using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.Abstractions;
using Seren.Application.Audio;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;
using Shouldly;
using Xunit;

using AppICharacterRepository = Seren.Application.Abstractions.ICharacterRepository;

namespace Seren.Application.Tests.Audio;

public sealed class SubmitVoiceInputHandlerTests
{
    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Handle_WithActiveCharacter_ShouldTranscribeAndForwardText()
    {
        var ct = TestContext.Current.CancellationToken;

        var character = new Character(
            Id: Guid.NewGuid(),
            Name: "Airi",
            SystemPrompt: "You are a helpful assistant.",
            VrmAssetPath: null,
            Voice: "default-voice",
            AgentId: "agent-1",
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var stt = new FakeSttProvider("Hello there!");
        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("Hi from AI!", null), new ChatStreamDelta(null, "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();

        var handler = new SubmitVoiceInputHandler(
            stt, chat, repository, hub, SessionKeyProvider, NullLogger<SubmitVoiceInputHandler>.Instance);

        var command = new SubmitVoiceInputCommand([1, 2, 3], "wav");

        var result = await handler.Handle(command, ct);

        result.ShouldBe("Hello there!");
        chat.CapturedMessage.ShouldBe("Hello there!");
        chat.CapturedAgentId.ShouldBe("agent-1");
        chat.CapturedSessionKey.ShouldBe(TestSessionKey);
    }

    [Fact]
    public async Task Handle_WithoutTtsProvider_ShouldOnlyBroadcastChatChunks()
    {
        var ct = TestContext.Current.CancellationToken;

        var stt = new FakeSttProvider("Test input");
        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("Response", null), new ChatStreamDelta(null, "stop")));
        var repository = new FakeCharacterRepository(null);
        var hub = new FakeSerenHub();

        var handler = new SubmitVoiceInputHandler(
            stt, chat, repository, hub, SessionKeyProvider, NullLogger<SubmitVoiceInputHandler>.Instance);

        await handler.Handle(new SubmitVoiceInputCommand([1, 2, 3], "wav"), ct);

        hub.BroadcastEnvelopes.ShouldNotBeEmpty();
        hub.BroadcastEnvelopes.Any(e => e.Type == EventTypes.AudioPlaybackChunk).ShouldBeFalse();
        hub.BroadcastEnvelopes.Any(e => e.Type == EventTypes.AudioLipsyncFrame).ShouldBeFalse();

        hub.BroadcastEnvelopes.Count(e => e.Type == EventTypes.OutputChatChunk).ShouldBe(1);
        hub.BroadcastEnvelopes.ShouldContain(e => e.Type == EventTypes.OutputChatEnd);
    }

    [Fact]
    public async Task Handle_WithTtsProvider_ShouldBroadcastAudioAndLipsyncEvents()
    {
        var ct = TestContext.Current.CancellationToken;

        var character = new Character(
            Id: Guid.NewGuid(),
            Name: "Airi",
            SystemPrompt: "You are happy.",
            VrmAssetPath: null,
            Voice: "voice-1",
            AgentId: null,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var stt = new FakeSttProvider("Hi");
        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("I am glad!", null), new ChatStreamDelta(null, "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();
        var tts = new FakeTtsProvider(
        [
            new([4, 5, 6], "pcm", [new VisemeFrame("aa", 0f, 0.1f), new VisemeFrame("O", 0.1f, 0.15f)]),
        ]);

        var handler = new SubmitVoiceInputHandler(
            stt, chat, repository, hub, SessionKeyProvider, NullLogger<SubmitVoiceInputHandler>.Instance, tts);

        await handler.Handle(new SubmitVoiceInputCommand([1, 2, 3], "wav"), ct);

        hub.BroadcastEnvelopes.Count(e => e.Type == EventTypes.AudioPlaybackChunk).ShouldBe(1);
        hub.BroadcastEnvelopes.Count(e => e.Type == EventTypes.AudioLipsyncFrame).ShouldBe(2);
        hub.BroadcastEnvelopes.ShouldContain(e => e.Type == EventTypes.OutputChatEnd);
    }

    [Fact]
    public async Task Handle_SttReturnsEmpty_ShouldStillForwardEmptyText()
    {
        var ct = TestContext.Current.CancellationToken;

        var stt = new FakeSttProvider(""); // empty transcription
        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("I heard nothing.", null), new ChatStreamDelta(null, "stop")));
        var repository = new FakeCharacterRepository(null);
        var hub = new FakeSerenHub();

        var handler = new SubmitVoiceInputHandler(
            stt, chat, repository, hub, SessionKeyProvider, NullLogger<SubmitVoiceInputHandler>.Instance);

        var result = await handler.Handle(new SubmitVoiceInputCommand([1, 2, 3], "wav"), ct);

        result.ShouldBeEmpty();
        chat.CapturedMessage.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WithEmotionMarkers_ShouldBroadcastChatChunkAndAvatarEmotion()
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

        var stt = new FakeSttProvider("Hi");
        var chat = new FakeOpenClawChat(Streams(
            new ChatStreamDelta("I am so <emotion:joy>happy!", null),
            new ChatStreamDelta(null, "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();

        var handler = new SubmitVoiceInputHandler(
            stt, chat, repository, hub, SessionKeyProvider, NullLogger<SubmitVoiceInputHandler>.Instance);

        await handler.Handle(new SubmitVoiceInputCommand([1, 2, 3], "wav"), ct);

        var chunkEnvelope = hub.BroadcastEnvelopes.FirstOrDefault(e => e.Type == EventTypes.OutputChatChunk);
        chunkEnvelope.ShouldNotBeNull();
        var chunkPayload = JsonSerializer.Deserialize<ChatChunkPayload>(
            chunkEnvelope.Data.GetRawText(), CamelCaseJson);
        chunkPayload!.Content.ShouldContain("happy!");
        chunkPayload.Content.ShouldNotContain("<emotion:joy>");

        var emotionEnvelope = hub.BroadcastEnvelopes.FirstOrDefault(e => e.Type == EventTypes.AvatarEmotion);
        emotionEnvelope.ShouldNotBeNull();
        var emotionPayload = JsonSerializer.Deserialize<AvatarEmotionPayload>(
            emotionEnvelope.Data.GetRawText(), CamelCaseJson);
        emotionPayload!.Emotion.ShouldBe("joy");

        hub.BroadcastEnvelopes.ShouldContain(e => e.Type == EventTypes.OutputChatEnd);
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

        var stt = new FakeSttProvider("hello");
        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("ok", "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();
        var handler = new SubmitVoiceInputHandler(
            stt, chat, repository, hub, SessionKeyProvider, NullLogger<SubmitVoiceInputHandler>.Instance);

        await handler.Handle(new SubmitVoiceInputCommand([1, 2, 3], Model: "openai/gpt-4o-mini"), ct);

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

        var stt = new FakeSttProvider("hello");
        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("ok", "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();
        var handler = new SubmitVoiceInputHandler(
            stt, chat, repository, hub, SessionKeyProvider, NullLogger<SubmitVoiceInputHandler>.Instance);

        await handler.Handle(new SubmitVoiceInputCommand([1, 2, 3]), ct);

        chat.CapturedAgentId.ShouldBe("ollama/default");
    }

    [Fact]
    public async Task Handle_WithNoOverrideAndNoCharacterAgentId_PassesNullAgentId()
    {
        var ct = TestContext.Current.CancellationToken;
        var stt = new FakeSttProvider("hello");
        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("ok", "stop")));
        var repository = new FakeCharacterRepository(null);
        var hub = new FakeSerenHub();
        var handler = new SubmitVoiceInputHandler(
            stt, chat, repository, hub, SessionKeyProvider, NullLogger<SubmitVoiceInputHandler>.Instance);

        await handler.Handle(new SubmitVoiceInputCommand([1, 2, 3]), ct);

        chat.CapturedAgentId.ShouldBeNull();
    }

    // --- Fakes ---

    private static ChatStreamDelta[] Streams(params ChatStreamDelta[] deltas) => deltas;

    private const string TestSessionKey = "seren-test";
    private static readonly IChatSessionKeyProvider SessionKeyProvider = new FakeSessionKeyProvider(TestSessionKey);

    private sealed class FakeSessionKeyProvider(string key) : IChatSessionKeyProvider
    {
        public string MainSessionKey { get; } = key;
        public Task<string> RotateAsync(CancellationToken cancellationToken) => Task.FromResult(MainSessionKey);
    }

    private sealed class FakeSttProvider : ISttProvider
    {
        private readonly string _text;
        public FakeSttProvider(string text) { _text = text; }

        public Task<SttResult> TranscribeAsync(byte[] audioData, string format, CancellationToken ct = default) =>
            Task.FromResult(new SttResult(_text, Language: "en", Confidence: 0.95f));
    }

    private sealed class FakeTtsProvider : ITtsProvider
    {
        private readonly List<TtsChunk> _chunks;
        public FakeTtsProvider(List<TtsChunk> chunks) { _chunks = chunks; }

        public async IAsyncEnumerable<TtsChunk> SynthesizeAsync(
            string text,
            string? voice = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var chunk in _chunks)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
        }
    }

    private sealed class FakeOpenClawChat : IOpenClawChat
    {
        private readonly ChatStreamDelta[] _deltas;
        public string? CapturedSessionKey { get; private set; }
        public string? CapturedMessage { get; private set; }
        public string? CapturedAgentId { get; private set; }

        public FakeOpenClawChat(ChatStreamDelta[] deltas) { _deltas = deltas; }

        public Task<string> StartAsync(string sessionKey, string message, string? agentId, CancellationToken cancellationToken)
        {
            CapturedSessionKey = sessionKey;
            CapturedMessage = message;
            CapturedAgentId = agentId;
            return Task.FromResult("run-fake");
        }

        public IAsyncEnumerable<ChatStreamDelta> SubscribeAsync(string runId, CancellationToken cancellationToken) =>
            EnumerateAsync(_deltas, cancellationToken);

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
        public FakeCharacterRepository(Character? active) { _active = active; }

        public Task<Character?> GetActiveAsync(CancellationToken cancellationToken) => Task.FromResult(_active);
        public Task<Character?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_active);

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
