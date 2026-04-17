using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly ISerenHub _hub;
    private readonly ChatOptions _chatOptions;
    private readonly ILogger<SubmitVoiceInputHandler> _logger;

    public SubmitVoiceInputHandler(
        ISttProvider sttProvider,
        IOpenClawClient openClawClient,
        ICharacterRepository characterRepository,
        ISerenHub hub,
        IOptions<ChatOptions> chatOptions,
        ILogger<SubmitVoiceInputHandler> logger,
        ITtsProvider? ttsProvider = null)
    {
        _sttProvider = sttProvider;
        _ttsProvider = ttsProvider;
        _openClawClient = openClawClient;
        _characterRepository = characterRepository;
        _hub = hub;
        _chatOptions = chatOptions.Value;
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

        // 3. Stream chat from OpenClaw — session key delegates history management.
        // `markerBuffer` holds trailing characters that might be the start of a
        // marker whose closing ">" lands in the next chunk (e.g. "<emoti" then
        // "on:joy>"). We only parse up to the safe boundary each time.
        var fullContent = string.Empty;
        var markerBuffer = string.Empty;

        // Model precedence: explicit command override (Settings UI) →
        // active character's default agent → gateway DefaultAgentId
        // (handled downstream in OpenClawRestClient).
        var effectiveAgentId = command.Model ?? character?.AgentId;

        await foreach (var chunk in _openClawClient.StreamChatAsync(
            messages, effectiveAgentId, sessionKey, cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                markerBuffer += chunk.Content;
                var safeCut = SafeMarkerBoundary(markerBuffer);
                var ready = markerBuffer[..safeCut];
                markerBuffer = markerBuffer[safeCut..];

                if (!string.IsNullOrEmpty(ready))
                {
                    await BroadcastParsed(ready, characterId, cancellationToken);
                    fullContent += LlmMarkerParser.Parse(ready).CleanText;
                }
            }

            if (chunk.FinishReason is not null)
            {
                break;
            }
        }

        // Flush any dangling buffered text so an unclosed "<..." isn't lost.
        if (!string.IsNullOrEmpty(markerBuffer))
        {
            await BroadcastParsed(markerBuffer, characterId, cancellationToken);
            fullContent += LlmMarkerParser.Parse(markerBuffer).CleanText;
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

    private List<ChatMessage> BuildMessages(string text, Character? character)
    {
        var messages = new List<ChatMessage>(
            SystemPromptBuilder.Build(character, _chatOptions.DefaultSystemPrompt));
        messages.Add(new ChatMessage("user", text));
        return messages;
    }

    private async Task BroadcastParsed(
        string text,
        string? characterId,
        CancellationToken ct)
    {
        var parseResult = LlmMarkerParser.Parse(text);

        if (!string.IsNullOrEmpty(parseResult.CleanText))
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    EventTypes.OutputChatChunk,
                    new ChatChunkPayload { Content = parseResult.CleanText, CharacterId = characterId }),
                null, ct).ConfigureAwait(false);
        }

        foreach (var emotion in parseResult.Emotions)
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    EventTypes.AvatarEmotion,
                    new AvatarEmotionPayload { Emotion = emotion.Emotion, CharacterId = characterId }),
                null, ct).ConfigureAwait(false);
        }

        foreach (var action in parseResult.Actions)
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    EventTypes.AvatarAction,
                    new AvatarActionPayload { Action = action.Action, CharacterId = characterId }),
                null, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Same contract as <c>SendTextMessageHandler.SafeMarkerBoundary</c>.</summary>
    private static int SafeMarkerBoundary(string buffer)
    {
        var lastOpen = buffer.LastIndexOf('<');
        if (lastOpen < 0)
        {
            return buffer.Length;
        }
        var matchingClose = buffer.IndexOf('>', lastOpen);
        if (matchingClose >= 0)
        {
            return buffer.Length;
        }
        return lastOpen;
    }

    private static WebSocketEnvelope CreateEnvelope(string eventType, object payload)
    {
        // Wire format: camelCase so the SDK's TypeScript types match.
        var json = JsonSerializer.Serialize(payload, payload.GetType(), CamelCaseOptions);
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

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly ModuleIdentityDto HubSource = new() { Id = "seren-hub", PluginId = "seren" };
}
