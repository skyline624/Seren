using System.Runtime.CompilerServices;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;

namespace Seren.Application.Audio;

/// <summary>
/// Handles <see cref="SubmitVoiceInputCommand"/> by transcribing audio via
/// <see cref="ISttProvider"/>, streaming a chat completion from OpenClaw Gateway,
/// and broadcasting chunks, emotion markers, TTS audio, viseme frames, and
/// the stream-end event to all connected peers via <see cref="ISerenHub"/>.
/// </summary>
public sealed class SubmitVoiceInputHandler : ICommandHandler<SubmitVoiceInputCommand, string>
{
    private readonly ISttProvider _sttProvider;
    private readonly ITtsProvider? _ttsProvider;
    private readonly IOpenClawClient _openClawClient;
    private readonly ICharacterRepository _characterRepository;
    private readonly ISerenHub _hub;
    private readonly ILogger<SubmitVoiceInputHandler> _logger;

    public SubmitVoiceInputHandler(
        ISttProvider sttProvider,
        IOpenClawClient openClawClient,
        ICharacterRepository characterRepository,
        ISerenHub hub,
        ILogger<SubmitVoiceInputHandler> logger,
        ITtsProvider? ttsProvider = null)
    {
        _sttProvider = sttProvider;
        _ttsProvider = ttsProvider;
        _openClawClient = openClawClient;
        _characterRepository = characterRepository;
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

        // 2. Get active character and build messages
        var character = await _characterRepository.GetActiveAsync(cancellationToken);
        var messages = BuildMessages(text, character);
        var characterId = character?.Id.ToString();

        // 3. Stream chat from OpenClaw and broadcast chunks
        var fullContent = string.Empty;

        await foreach (var chunk in _openClawClient.StreamChatAsync(messages, character?.AgentId, cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                var parseResult = LlmMarkerParser.Parse(chunk.Content);

                // Broadcast chat chunk
                var chatEnvelope = CreateEnvelope(
                    EventTypes.OutputChatChunk,
                    new ChatChunkPayload { Content = parseResult.CleanText, CharacterId = characterId });

                await _hub.BroadcastAsync(chatEnvelope, null, cancellationToken);

                // Broadcast emotion markers
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

        _logger.LogInformation(
            "Voice input stream completed for session {SessionId}",
            command.SessionId);

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
            // Broadcast audio playback chunk
            var playbackEnvelope = CreateEnvelope(
                EventTypes.AudioPlaybackChunk,
                new AudioPlaybackPayload { Audio = ttsChunk.Audio, Format = ttsChunk.Format, CharacterId = characterId });

            await _hub.BroadcastAsync(playbackEnvelope, null, ct);

            // Broadcast viseme frames for lip sync
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

    private static List<ChatMessage> BuildMessages(string text, Domain.Entities.Character? character)
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
