using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Application.Audio;
using Seren.Application.Chat;
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
    private static IOptions<ChatOptions> EmptyChatOptions()
        => Options.Create(new ChatOptions { DefaultSystemPrompt = string.Empty });

    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Handle_WithActiveCharacter_ShouldTranscribeAndSendToOpenClaw()
    {
        // arrange
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

        var sttProvider = new FakeSttProvider("Hello there!");
        var client = new FakeOpenClawClient([new("Hi from AI!", null), new(null, "stop")]);
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();

        var handler = new SubmitVoiceInputHandler(
            sttProvider, client, repository, hub, EmptyChatOptions(), NullLogger<SubmitVoiceInputHandler>.Instance);

        var command = new SubmitVoiceInputCommand([1, 2, 3], "wav");

        // act
        var result = await handler.Handle(command, ct);

        // assert — transcription text is returned
        result.ShouldBe("Hello there!");

        // StreamChatAsync was called with system prompt + user message
        client.CapturedMessages.ShouldNotBeNull();
        client.CapturedMessages!.Count.ShouldBe(2);
        client.CapturedMessages[0].Role.ShouldBe("system");
        client.CapturedMessages[0].Content.ShouldBe("You are a helpful assistant.");
        client.CapturedMessages[1].Role.ShouldBe("user");
        client.CapturedMessages[1].Content.ShouldBe("Hello there!");
        client.CapturedAgentId.ShouldBe("agent-1");
    }

    [Fact]
    public async Task Handle_WithoutTtsProvider_ShouldOnlyBroadcastChatChunks()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;

        var sttProvider = new FakeSttProvider("Test input");
        var client = new FakeOpenClawClient([new("Response", null), new(null, "stop")]);
        var repository = new FakeCharacterRepository(null);
        var hub = new FakeSerenHub();

        var handler = new SubmitVoiceInputHandler(
            sttProvider, client, repository, hub, EmptyChatOptions(), NullLogger<SubmitVoiceInputHandler>.Instance);

        var command = new SubmitVoiceInputCommand([1, 2, 3], "wav");

        // act
        await handler.Handle(command, ct);

        // assert — no audio events, only chat chunk + chat end
        hub.BroadcastEnvelopes.ShouldNotBeEmpty();
        hub.BroadcastEnvelopes.Any(e => e.Type == EventTypes.AudioPlaybackChunk).ShouldBeFalse();
        hub.BroadcastEnvelopes.Any(e => e.Type == EventTypes.AudioLipsyncFrame).ShouldBeFalse();

        var chatChunks = hub.BroadcastEnvelopes.Where(e => e.Type == EventTypes.OutputChatChunk).ToList();
        chatChunks.Count.ShouldBe(1);

        var chatEnd = hub.BroadcastEnvelopes.FirstOrDefault(e => e.Type == EventTypes.OutputChatEnd);
        chatEnd.ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_WithTtsProvider_ShouldBroadcastAudioAndLipsyncEvents()
    {
        // arrange
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

        var sttProvider = new FakeSttProvider("Hi");
        var client = new FakeOpenClawClient([new("I am glad!", null), new(null, "stop")]);
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();
        var ttsProvider = new FakeTtsProvider(
        [
            new([4, 5, 6], "pcm", [new VisemeFrame("aa", 0f, 0.1f), new VisemeFrame("O", 0.1f, 0.15f)]),
        ]);

        var handler = new SubmitVoiceInputHandler(
            sttProvider, client, repository, hub, EmptyChatOptions(), NullLogger<SubmitVoiceInputHandler>.Instance, ttsProvider);

        var command = new SubmitVoiceInputCommand([1, 2, 3], "wav");

        // act
        await handler.Handle(command, ct);

        // assert — should have audio playback + lipsync events
        var playbackEnvelopes = hub.BroadcastEnvelopes.Where(e => e.Type == EventTypes.AudioPlaybackChunk).ToList();
        playbackEnvelopes.Count.ShouldBe(1);

        var lipsyncEnvelopes = hub.BroadcastEnvelopes.Where(e => e.Type == EventTypes.AudioLipsyncFrame).ToList();
        lipsyncEnvelopes.Count.ShouldBe(2); // two viseme frames

        var chatEnd = hub.BroadcastEnvelopes.FirstOrDefault(e => e.Type == EventTypes.OutputChatEnd);
        chatEnd.ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_SttReturnsEmpty_ShouldStillSendToOpenClaw()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;

        var sttProvider = new FakeSttProvider(""); // empty transcription
        var client = new FakeOpenClawClient([new("I heard nothing.", null), new(null, "stop")]);
        var repository = new FakeCharacterRepository(null);
        var hub = new FakeSerenHub();

        var handler = new SubmitVoiceInputHandler(
            sttProvider, client, repository, hub, EmptyChatOptions(), NullLogger<SubmitVoiceInputHandler>.Instance);

        var command = new SubmitVoiceInputCommand([1, 2, 3], "wav");

        // act
        var result = await handler.Handle(command, ct);

        // assert — empty text still sent to OpenClaw
        result.ShouldBeEmpty();
        client.CapturedMessages.ShouldNotBeNull();
        client.CapturedMessages!.Count.ShouldBe(1);
        client.CapturedMessages[0].Role.ShouldBe("user");
        client.CapturedMessages[0].Content.ShouldBeEmpty();
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

        var sttProvider = new FakeSttProvider("Hi");
        var client = new FakeOpenClawClient([new("I am so <emotion:joy>happy!", null), new(null, "stop")]);
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();

        var handler = new SubmitVoiceInputHandler(
            sttProvider, client, repository, hub, EmptyChatOptions(), NullLogger<SubmitVoiceInputHandler>.Instance);

        var command = new SubmitVoiceInputCommand([1, 2, 3], "wav");

        // act
        await handler.Handle(command, ct);

        // assert — should have: 1 chat chunk + 1 avatar emotion + 1 chat end
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

        var endEnvelope = hub.BroadcastEnvelopes.FirstOrDefault(e => e.Type == EventTypes.OutputChatEnd);
        endEnvelope.ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_WithExplicitModelOverride_PassesThatModelToOpenClaw()
    {
        // arrange — character has its own AgentId, command overrides it
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
        var client = new FakeOpenClawClient([new("ok", "stop")]);
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();
        var handler = new SubmitVoiceInputHandler(
            stt, client, repository, hub, EmptyChatOptions(), NullLogger<SubmitVoiceInputHandler>.Instance);

        // act
        await handler.Handle(
            new SubmitVoiceInputCommand([1, 2, 3], Model: "openai/gpt-4o-mini"), ct);

        // assert
        client.CapturedAgentId.ShouldBe("openai/gpt-4o-mini");
    }

    [Fact]
    public async Task Handle_WithoutModelOverride_FallsBackToCharacterAgentId()
    {
        // arrange
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
        var client = new FakeOpenClawClient([new("ok", "stop")]);
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();
        var handler = new SubmitVoiceInputHandler(
            stt, client, repository, hub, EmptyChatOptions(), NullLogger<SubmitVoiceInputHandler>.Instance);

        // act
        await handler.Handle(new SubmitVoiceInputCommand([1, 2, 3]), ct);

        // assert
        client.CapturedAgentId.ShouldBe("ollama/default");
    }

    [Fact]
    public async Task Handle_WithNoOverrideAndNoCharacterAgentId_PassesNullToOpenClaw()
    {
        // arrange — no character, no override
        var ct = TestContext.Current.CancellationToken;
        var stt = new FakeSttProvider("hello");
        var client = new FakeOpenClawClient([new("ok", "stop")]);
        var repository = new FakeCharacterRepository(null);
        var hub = new FakeSerenHub();
        var handler = new SubmitVoiceInputHandler(
            stt, client, repository, hub, EmptyChatOptions(), NullLogger<SubmitVoiceInputHandler>.Instance);

        // act
        await handler.Handle(new SubmitVoiceInputCommand([1, 2, 3]), ct);

        // assert
        client.CapturedAgentId.ShouldBeNull();
    }

    // --- Fakes ---

    private sealed class FakeSttProvider : ISttProvider
    {
        private readonly string _text;

        public FakeSttProvider(string text)
        {
            _text = text;
        }

        public Task<SttResult> TranscribeAsync(byte[] audioData, string format, CancellationToken ct = default)
        {
            return Task.FromResult(new SttResult(_text, Language: "en", Confidence: 0.95f));
        }
    }

    private sealed class FakeTtsProvider : ITtsProvider
    {
        private readonly List<TtsChunk> _chunks;

        public FakeTtsProvider(List<TtsChunk> chunks)
        {
            _chunks = chunks;
        }

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
            string? sessionKey = null,
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

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
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
