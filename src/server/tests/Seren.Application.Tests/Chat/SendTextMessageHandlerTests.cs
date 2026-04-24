using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;
using Shouldly;
using Xunit;

using AppICharacterRepository = Seren.Application.Abstractions.ICharacterRepository;
using Seren.Application.Chat.Attachments;
using Seren.Application.Tests.Chat.Attachments;

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
            AvatarModelPath: null,
            Voice: null,
            AgentId: "agent-1",
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("Hello!", null), new ChatStreamDelta(null, "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();

        var pipeline = BuildPipeline(chat, hub);
        var handler = new SendTextMessageHandler(
            pipeline, repository, hub, SessionKeyProvider,
            new AttachmentValidator(),
            new AttachmentTextExtractorRegistry([new PlainTextExtractor(), new PdfTextExtractor()]),
            NullLogger<SendTextMessageHandler>.Instance);

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

        var pipeline = BuildPipeline(chat, hub);
        var handler = new SendTextMessageHandler(
            pipeline, repository, hub, SessionKeyProvider,
            new AttachmentValidator(),
            new AttachmentTextExtractorRegistry([new PlainTextExtractor(), new PdfTextExtractor()]),
            NullLogger<SendTextMessageHandler>.Instance);

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
            AvatarModelPath: null,
            Voice: null,
            AgentId: null,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var chat = new FakeOpenClawChat(Streams(
            new ChatStreamDelta("I am so <emotion:joy>happy to see you!", null)));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();

        var pipeline = BuildPipeline(chat, hub);
        var handler = new SendTextMessageHandler(
            pipeline, repository, hub, SessionKeyProvider,
            new AttachmentValidator(),
            new AttachmentTextExtractorRegistry([new PlainTextExtractor(), new PdfTextExtractor()]),
            NullLogger<SendTextMessageHandler>.Instance);

        await handler.Handle(new SendTextMessageCommand("Hi"), ct);

        // 4 = user-echo + chunk + emotion + chat-end.
        hub.BroadcastEnvelopes.Count.ShouldBe(4);

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
        var pipeline = BuildPipeline(chat, hub);
        var handler = new SendTextMessageHandler(
            pipeline, repository, hub, SessionKeyProvider,
            new AttachmentValidator(),
            new AttachmentTextExtractorRegistry([new PlainTextExtractor(), new PdfTextExtractor()]),
            NullLogger<SendTextMessageHandler>.Instance);

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
            AvatarModelPath: null,
            Voice: null,
            AgentId: "ollama/default",
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("ok", "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();
        var pipeline = BuildPipeline(chat, hub);
        var handler = new SendTextMessageHandler(
            pipeline, repository, hub, SessionKeyProvider,
            new AttachmentValidator(),
            new AttachmentTextExtractorRegistry([new PlainTextExtractor(), new PdfTextExtractor()]),
            NullLogger<SendTextMessageHandler>.Instance);

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
            AvatarModelPath: null,
            Voice: null,
            AgentId: "ollama/default",
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("ok", "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();
        var pipeline = BuildPipeline(chat, hub);
        var handler = new SendTextMessageHandler(
            pipeline, repository, hub, SessionKeyProvider,
            new AttachmentValidator(),
            new AttachmentTextExtractorRegistry([new PlainTextExtractor(), new PdfTextExtractor()]),
            NullLogger<SendTextMessageHandler>.Instance);

        await handler.Handle(new SendTextMessageCommand("Hi"), ct);

        chat.CapturedAgentId.ShouldBe("ollama/default");
    }

    [Fact]
    public async Task Handle_WithImageAttachment_ForwardsToOpenClaw_WithoutMutatingText()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, chat, hub) = BuildHandlerWithAttachments(null);

        var jpeg = AttachmentFixtures.MinimalJpeg();
        var dto = AttachmentFixtures.AsDto("image/jpeg", "photo.jpg", jpeg);

        await handler.Handle(
            new SendTextMessageCommand("Look at this", Attachments: [dto]), ct);

        chat.CapturedMessage.ShouldBe("Look at this");
        chat.CapturedImageAttachments.ShouldNotBeNull();
        chat.CapturedImageAttachments!.Count.ShouldBe(1);
        chat.CapturedImageAttachments[0].MimeType.ShouldBe("image/jpeg");
        chat.CapturedImageAttachments[0].FileName.ShouldBe("photo.jpg");
        chat.CapturedImageAttachments[0].Content.ShouldBe(jpeg);
    }

    [Fact]
    public async Task Handle_WithPdfAttachment_ExtractsTextAndFoldsIntoMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, chat, _) = BuildHandlerWithAttachments(null);

        var pdf = AttachmentFixtures.MinimalPdf("report contents 12345");
        var dto = AttachmentFixtures.AsDto("application/pdf", "report.pdf", pdf);

        await handler.Handle(
            new SendTextMessageCommand("Summarize this", Attachments: [dto]), ct);

        chat.CapturedMessage!.ShouldStartWith("Summarize this");
        chat.CapturedMessage!.ShouldContain("--- Attachment: report.pdf (application/pdf) ---");
        chat.CapturedMessage!.ShouldContain("report contents 12345");
        // Documents never reach the OpenClaw attachments array — only images do.
        (chat.CapturedImageAttachments is null || chat.CapturedImageAttachments.Count == 0).ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WithMixedImageAndPdf_PartitionsCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, chat, _) = BuildHandlerWithAttachments(null);

        var image = AttachmentFixtures.AsDto("image/png", "diagram.png", AttachmentFixtures.MinimalPng());
        var pdf = AttachmentFixtures.AsDto("application/pdf", "notes.pdf",
            AttachmentFixtures.MinimalPdf("inline pdf body"));

        await handler.Handle(
            new SendTextMessageCommand("Here are both", Attachments: [image, pdf]), ct);

        chat.CapturedImageAttachments!.Count.ShouldBe(1);
        chat.CapturedImageAttachments[0].FileName.ShouldBe("diagram.png");
        chat.CapturedMessage!.ShouldContain("--- Attachment: notes.pdf");
        chat.CapturedMessage!.ShouldContain("inline pdf body");
    }

    [Fact]
    public async Task Handle_WithInvalidAttachment_EmitsTypedError_DoesNotCallOpenClaw()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, chat, hub) = BuildHandlerWithAttachments(null);

        // Base64 content decodes fine but the magic bytes don't match image/jpeg.
        var spoofed = new ChatAttachmentDto
        {
            MimeType = "image/jpeg",
            FileName = "fake.jpg",
            ByteSize = 5,
            Content = Convert.ToBase64String("hello"u8.ToArray()),
        };

        await handler.Handle(
            new SendTextMessageCommand("", PeerId: "peer-42", Attachments: [spoofed]), ct);

        // Validation failure → OpenClaw never called, but a typed error frame went to the originator.
        chat.CapturedMessage.ShouldBeNull();
        hub.SendEnvelopes.Count.ShouldBe(1);
        hub.SendEnvelopes[0].Peer.Value.ShouldBe("peer-42");
        hub.SendEnvelopes[0].Envelope.Type.ShouldBe(EventTypes.Error);
        var errorPayload = JsonSerializer.Deserialize<ErrorPayload>(
            hub.SendEnvelopes[0].Envelope.Data.GetRawText(), CamelCaseJson);
        errorPayload!.Code.ShouldBe(AttachmentValidationError.MagicMismatch);
    }

    [Fact]
    public async Task Handle_WhenPdfExtractionFails_InsertsNoteAndContinues()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, chat, _) = BuildHandlerWithAttachments(null);

        // Valid PDF magic header but truncated body → PdfPig throws during extraction.
        var corrupted = new byte[64];
        "%PDF-1.4\n"u8.CopyTo(corrupted);
        for (var i = 9; i < corrupted.Length; i++)
        {
            corrupted[i] = 0xFF;
        }
        var dto = AttachmentFixtures.AsDto("application/pdf", "broken.pdf", corrupted);

        await handler.Handle(
            new SendTextMessageCommand("Read this", Attachments: [dto]), ct);

        // The message still reaches OpenClaw, with a note explaining the failure.
        chat.CapturedMessage!.ShouldStartWith("Read this");
        chat.CapturedMessage!.ShouldContain("--- Attachment: broken.pdf");
        chat.CapturedMessage!.ShouldContain("could not be parsed");
    }

    [Fact]
    public async Task Handle_WithAttachments_EchoIncludesMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, _, hub) = BuildHandlerWithAttachments(null);

        var dto = AttachmentFixtures.AsDto("image/jpeg", "pic.jpg", AttachmentFixtures.MinimalJpeg());

        await handler.Handle(
            new SendTextMessageCommand("Hi", Attachments: [dto]), ct);

        var echo = hub.BroadcastEnvelopes.FirstOrDefault(e => e.Type == EventTypes.OutputChatUser);
        echo.ShouldNotBeNull();
        var payload = JsonSerializer.Deserialize<UserEchoPayload>(echo.Data.GetRawText(), CamelCaseJson);
        payload!.Attachments.ShouldNotBeNull();
        payload.Attachments!.Count.ShouldBe(1);
        payload.Attachments[0].FileName.ShouldBe("pic.jpg");
        payload.Attachments[0].MimeType.ShouldBe("image/jpeg");
        payload.Attachments[0].AttachmentId.ShouldNotBeNullOrEmpty();
    }

    /// <summary>
    /// Boilerplate-free handler builder shared by the attachment tests.
    /// Wires a fake chat + hub + active character (optional) into the real
    /// pipeline with the concrete AttachmentValidator + extractor registry,
    /// and returns all three so assertions can probe behaviour on each.
    /// </summary>
    private static (SendTextMessageHandler Handler, FakeOpenClawChat Chat, FakeSerenHub Hub)
        BuildHandlerWithAttachments(Character? character)
    {
        var chat = new FakeOpenClawChat(Streams(new ChatStreamDelta("ok", "stop")));
        var repository = new FakeCharacterRepository(character);
        var hub = new FakeSerenHub();
        var pipeline = BuildPipeline(chat, hub);
        var handler = new SendTextMessageHandler(
            pipeline, repository, hub, SessionKeyProvider,
            new AttachmentValidator(),
            new AttachmentTextExtractorRegistry([new PlainTextExtractor(), new PdfTextExtractor()]),
            NullLogger<SendTextMessageHandler>.Instance);
        return (handler, chat, hub);
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

        var pipeline = BuildPipeline(chat, hub);
        var handler = new SendTextMessageHandler(
            pipeline, repository, hub, SessionKeyProvider,
            new AttachmentValidator(),
            new AttachmentTextExtractorRegistry([new PlainTextExtractor(), new PdfTextExtractor()]),
            NullLogger<SendTextMessageHandler>.Instance);

        await handler.Handle(new SendTextMessageCommand("Hi"), ct);

        // 4 = user-echo + 2 chunks + chat-end.
        hub.BroadcastEnvelopes.Count.ShouldBe(4);
        hub.BroadcastEnvelopes.Count(e => e.Type == EventTypes.OutputChatChunk).ShouldBe(2);
        hub.BroadcastEnvelopes.Count(e => e.Type == EventTypes.OutputChatEnd).ShouldBe(1);
        hub.BroadcastEnvelopes.Count(e => e.Type == EventTypes.OutputChatUser).ShouldBe(1);
    }

    private static ChatStreamDelta[] Streams(params ChatStreamDelta[] deltas) => deltas;

    private const string TestSessionKey = "seren-test";
    private static readonly IChatSessionKeyProvider SessionKeyProvider = new FakeSessionKeyProvider(TestSessionKey);

    // Default options use generous timeouts so non-timeout tests don't trip
    // them; dedicated timeout tests construct their own options inline.
    private static readonly IOptions<ChatStreamOptions> StreamOptions =
        Options.Create(new ChatStreamOptions
        {
            IdleTimeout = TimeSpan.FromSeconds(30),
            TotalTimeout = TimeSpan.FromMinutes(3),
        });

    private static IChatRunRegistry RunRegistry => new FakeChatRunRegistry();

    private static readonly IOptions<ChatResilienceOptions> ResilienceOptions =
        Options.Create(new ChatResilienceOptions
        {
            // Disable retry/fallback for handler tests — they assert marker
            // parsing behavior, not resilience semantics (those live in
            // ChatStreamPipelineTests).
            RetryOnIdleBeforeFirstChunk = 0,
            FallbackModels = new List<string>(),
        });

    /// <summary>
    /// Builds a real <see cref="ChatStreamPipeline"/> wired to the provided
    /// chat + hub stubs. Using the real pipeline keeps the handler tests
    /// end-to-end without duplicating marker-parsing assertions in
    /// pipeline tests.
    /// </summary>
    private static ChatStreamPipeline BuildPipeline(IOpenClawChat chat, ISerenHub hub)
    {
        return new ChatStreamPipeline(
            chat: chat,
            hub: hub,
            runRegistry: RunRegistry,
            streamOptions: StreamOptions,
            resilienceOptions: ResilienceOptions,
            metrics: new ChatStreamMetrics(),
            logger: NullLogger<ChatStreamPipeline>.Instance);
    }

    private sealed class FakeSessionKeyProvider(string key) : IChatSessionKeyProvider
    {
        public string MainSessionKey { get; } = key;
        public Task<string> RotateAsync(CancellationToken cancellationToken) => Task.FromResult(MainSessionKey);
    }

    private sealed class FakeChatRunRegistry : IChatRunRegistry
    {
        private readonly ConcurrentDictionary<string, string> _runs = new();
        public void Register(string sessionKey, string runId) => _runs[sessionKey] = runId;
        public void Unregister(string sessionKey, string runId)
            => _runs.TryRemove(new KeyValuePair<string, string>(sessionKey, runId));
        public string? GetActiveRun(string sessionKey)
            => _runs.TryGetValue(sessionKey, out var v) ? v : null;
    }

    private sealed class FakeOpenClawChat : IOpenClawChat
    {
        private readonly ChatStreamDelta[] _deltas;

        public string? CapturedSessionKey { get; private set; }
        public string? CapturedMessage { get; private set; }
        public string? CapturedAgentId { get; private set; }
        public string? PinnedSessionKey { get; private set; }
        public string? PinnedModel { get; private set; }
        public int PinCallCount { get; private set; }

        public FakeOpenClawChat(ChatStreamDelta[] deltas)
        {
            _deltas = deltas;
        }

        public Task PinSessionModelAsync(string sessionKey, string? model, CancellationToken cancellationToken)
        {
            PinnedSessionKey = sessionKey;
            PinnedModel = model;
            PinCallCount++;
            return Task.CompletedTask;
        }

        public Task<string> StartAsync(
            string sessionKey, string message, string? agentId, string? idempotencyKey,
            IReadOnlyList<ChatImageAttachment>? imageAttachments, CancellationToken cancellationToken)
        {
            CapturedSessionKey = sessionKey;
            CapturedMessage = message;
            CapturedAgentId = agentId;
            CapturedIdempotencyKey = idempotencyKey;
            CapturedImageAttachments = imageAttachments;
            return Task.FromResult(idempotencyKey ?? "run-fake");
        }

        public IReadOnlyList<ChatImageAttachment>? CapturedImageAttachments { get; private set; }

        public string? CapturedIdempotencyKey { get; private set; }
        public string? AbortedSessionKey { get; private set; }
        public string? AbortedRunId { get; private set; }

        public Task AbortAsync(string sessionKey, string runId, CancellationToken cancellationToken)
        {
            AbortedSessionKey = sessionKey;
            AbortedRunId = runId;
            return Task.CompletedTask;
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
        public List<(PeerId Peer, WebSocketEnvelope Envelope)> SendEnvelopes { get; } = [];

        public Task<bool> SendAsync(PeerId peerId, WebSocketEnvelope envelope, CancellationToken cancellationToken)
        {
            SendEnvelopes.Add((peerId, envelope));
            return Task.FromResult(true);
        }

        public Task<int> BroadcastAsync(WebSocketEnvelope envelope, PeerId? excluding, CancellationToken cancellationToken)
        {
            BroadcastEnvelopes.Add(envelope);
            return Task.FromResult(BroadcastEnvelopes.Count);
        }
    }
}
