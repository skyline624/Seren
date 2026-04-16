using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.Entities;

namespace Seren.Application.Audio;

/// <summary>
/// Handles <see cref="SubmitVoiceInputCommand"/> by transcribing audio via
/// <see cref="ISttProvider"/>, streaming a chat completion from OpenClaw Gateway,
/// and broadcasting chunks, emotion markers, TTS audio, viseme frames, and
/// the stream-end event to all connected peers via <see cref="ISerenHub"/>.
/// Conversation history is managed server-side by OpenClaw via session keys.
/// Messages are persisted locally for UI display purposes.
/// </summary>
public sealed class SubmitVoiceInputHandler : ICommandHandler<SubmitVoiceInputCommand, string>
{
    private readonly ISttProvider _sttProvider;
    private readonly ITtsProvider? _ttsProvider;
    private readonly IOpenClawClient _openClawClient;
    private readonly ICharacterRepository _characterRepository;
    private readonly IConversationRepository _conversationRepository;
    private readonly ISerenHub _hub;
    private readonly ILogger<SubmitVoiceInputHandler> _logger;

    public SubmitVoiceInputHandler(
        ISttProvider sttProvider,
        IOpenClawClient openClawClient,
        ICharacterRepository characterRepository,
        IConversationRepository conversationRepository,
        ISerenHub hub,
        ILogger<SubmitVoiceInputHandler> logger,
        ITtsProvider? ttsProvider = null)
    {
        _sttProvider = sttProvider;
        _ttsProvider = ttsProvider;
        _openClawClient = openClawClient;
        _characterRepository = characterRepository;
        _conversationRepository = conversationRepository;
        _hub = hub;
        _logger = logger;
    }

    public async ValueTask<string> Handle(SubmitVoiceInputCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Transcribe audio
        var transcription = await _sttProvider.TranscribeAsync(command.AudioData, command.Format, cancellationToken);
        var text = transcription.Text;

        _logger.LogInformation(
            "Voice input transcribed: {Text} (language={Language}, confidence={Confidence})",
            text, transcription.Language, transcription.Confidence);

        // 2. Get active character and prepare session
        var character = await _characterRepository.GetActiveAsync(cancellationToken);
        var sessionId = command.SessionId ?? Guid.NewGuid();
        var sessionKey = sessionId.ToString("N");

        // Build messages with only the current turn — OpenClaw maintains
        // conversation history server-side via the session key.
        var messages = BuildMessages(text, character);
        var characterId = character?.Id.ToString();

        // Persist user message locally for UI history
        var userMessage = ConversationMessage.Create(sessionId, "user", text, character?.Id);
        await _conversationRepository.AddAsync(userMessage, cancellationToken);

        // 3. Stream chat from OpenClaw — session key delegates history management
        var fullContent = string.Empty;

        await foreach (var chunk in _openClawClient.StreamChatAsync(
            messages, character?.AgentId, sessionKey, cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                var parseResult = LlmMarkerParser.Parse(chunk.Content);

                var chatEnvelope = CreateEnvelope(
                    EventTypes.OutputChatChunk,
                    new ChatChunkPayload { Content = parseResult.CleanText, CharacterId = characterId });

                await _hub.BroadcastAsync(chatEnvelope, null, cancellationToken);

                foreach (var emotion in parseResult.Emotions)
                {
                    var emotionEnvelope = CreateEnvelope(
                        EventTypes.AvatarEmotion,
                        new AvatarEmotionPayload { Emotion = emotion.Emotion, CharacterId = characterId });

                    await _hub.BroadcastAsync(emotionEnvelope, null, cancellationToken);
                }

                fullContent += parseResult.CleanText;
            }

            if (chunk.FinishReason is not null)
            {
                break;
            }
        }

        // 4. TTS synthesis if provider is available and we have content
        if (_ttsProvider is not null && !string.IsNullOrWhiteSpace(fullContent))
        {
            await SynthesizeAndBroadcast(fullContent, character?.Voice, characterId, cancellationToken);
        }

        // 5. Broadcast stream end
        var endEnvelope = CreateEnvelope(
            EventTypes.OutputChatEnd,
            new ChatEndPayload { CharacterId = characterId });

        await _hub.BroadcastAsync(endEnvelope, null, cancellationToken);

        // 6. Persist assistant response locally for UI history
        if (!string.IsNullOrWhiteSpace(fullContent))
        {
            var assistantMessage = ConversationMessage.Create(sessionId, "assistant", fullContent, character?.Id);
            await _conversationRepository.AddAsync(assistantMessage, cancellationToken);
        }

        _logger.LogInformation(
            "Voice input stream completed for session {SessionId}",
            sessionId);

        return text;
    }

    private async Task SynthesizeAndBroadcast(
        string text,
        string? voice,
        string? characterId,
        CancellationToken ct)
    {
        if (_ttsProvider is null)
        {
            return;
        }

        await foreach (var ttsChunk in _ttsProvider.SynthesizeAsync(text, voice, ct))
        {
            var playbackEnvelope = CreateEnvelope(
                EventTypes.AudioPlaybackChunk,
                new AudioPlaybackPayload { Audio = ttsChunk.Audio, Format = ttsChunk.Format, CharacterId = characterId });

            await _hub.BroadcastAsync(playbackEnvelope, null, ct);

            if (ttsChunk.Visemes is not null)
            {
                foreach (var viseme in ttsChunk.Visemes)
                {
                    var lipsyncEnvelope = CreateEnvelope(
                        EventTypes.AudioLipsyncFrame,
                        new LipsyncFramePayload
                        {
                            Viseme = viseme.Viseme,
                            StartTime = viseme.StartTime,
                            Duration = viseme.Duration,
                            Weight = viseme.Weight,
                            CharacterId = characterId,
                        });

                    await _hub.BroadcastAsync(lipsyncEnvelope, null, ct);
                }
            }
        }
    }

    private static List<ChatMessage> BuildMessages(string text, Character? character)
    {
        var messages = new List<ChatMessage>();

        if (character is { SystemPrompt.Length: > 0 })
        {
            messages.Add(new ChatMessage("system", character.SystemPrompt));
        }

        messages.Add(new ChatMessage("user", text));

        return messages;
    }

    private static WebSocketEnvelope CreateEnvelope(string eventType, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.Clone();

        return new WebSocketEnvelope
        {
            Type = eventType,
            Data = data,
            Metadata = new EventMetadata
            {
                Source = HubSource,
                Event = new EventIdentity { Id = Guid.NewGuid().ToString("N") },
            },
        };
    }

    private static readonly ModuleIdentityDto HubSource = new() { Id = "seren-hub", PluginId = "seren" };
}
