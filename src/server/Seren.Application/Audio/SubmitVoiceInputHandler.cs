using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;

namespace Seren.Application.Audio;

/// <summary>
/// Handles <see cref="SubmitVoiceInputCommand"/>: transcribes audio via
/// <see cref="ISttProvider"/>, then delegates chat streaming to
/// <see cref="IChatStreamPipeline"/> (inheriting idle/total timeout, retry,
/// and fallback semantics), and finally synthesises TTS + lipsync frames
/// on successful end via <see cref="ITtsProvider"/>.
/// </summary>
/// <remarks>
/// Before this refactor the voice handler had its own bare
/// <c>await foreach</c> on the chat stream — no timeout, no abort hook, no
/// retry. Reusing the shared pipeline is pure DRY and gives the voice path
/// the same enterprise-grade resilience as the text path for free.
/// </remarks>
public sealed class SubmitVoiceInputHandler : ICommandHandler<SubmitVoiceInputCommand, string>
{
    private readonly ISttProvider _sttProvider;
    private readonly ITtsProvider? _ttsProvider;
    private readonly IChatStreamPipeline _pipeline;
    private readonly ICharacterRepository _characterRepository;
    private readonly ISerenHub _hub;
    private readonly IChatSessionKeyProvider _sessionKeyProvider;
    private readonly ILogger<SubmitVoiceInputHandler> _logger;

    public SubmitVoiceInputHandler(
        ISttProvider sttProvider,
        IChatStreamPipeline pipeline,
        ICharacterRepository characterRepository,
        ISerenHub hub,
        IChatSessionKeyProvider sessionKeyProvider,
        ILogger<SubmitVoiceInputHandler> logger,
        ITtsProvider? ttsProvider = null)
    {
        _sttProvider = sttProvider;
        _ttsProvider = ttsProvider;
        _pipeline = pipeline;
        _characterRepository = characterRepository;
        _hub = hub;
        _sessionKeyProvider = sessionKeyProvider;
        _logger = logger;
    }

    public async ValueTask<string> Handle(SubmitVoiceInputCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Transcribe audio.
        var transcription = await _sttProvider.TranscribeAsync(command.AudioData, command.Format, cancellationToken);
        var text = transcription.Text;

        _logger.LogInformation(
            "Voice input transcribed: {Text} (language={Language}, confidence={Confidence})",
            text, transcription.Language, transcription.Confidence);

        // 2. Resolve session + character for the chat stream.
        var character = await _characterRepository.GetActiveAsync(cancellationToken);
        var sessionKey = _sessionKeyProvider.MainSessionKey;
        var characterId = character?.Id.ToString();
        var effectiveAgentId = command.Model ?? character?.AgentId;

        // Streaming state: marker boundary buffer + captured clean text for
        // post-stream TTS. A `fullContent` accumulator is voice-specific
        // because we need the complete answer before synthesising audio.
        var streamState = new VoiceStreamState();

        // 3. Delegate the whole run to the pipeline: it owns timeouts,
        // retries, fallback cascade, error broadcasts, chat:end, metrics.
        var request = new ChatStreamRequest(
            SessionKey: sessionKey,
            UserText: text,
            PrimaryModel: effectiveAgentId,
            ClientMessageId: null,   // voice doesn't carry a client-minted id
            CharacterId: characterId,
            OnContent: (content, ct) => OnContentAsync(streamState, characterId, content, ct),
            OnTeardown: ct => FlushStateAsync(streamState, characterId, ct),
            OnSuccess: ct => SynthesiseAsync(streamState.FullContent, character?.Voice, characterId, ct));

        await _pipeline.RunAsync(request, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Voice input stream completed for session {SessionKey}", sessionKey);

        return text;
    }

    private sealed class VoiceStreamState
    {
        public string MarkerBuffer = string.Empty;
        public string FullContent = string.Empty;
    }

    private async Task OnContentAsync(
        VoiceStreamState state, string? characterId, string content, CancellationToken ct)
    {
        state.MarkerBuffer += content;
        var safeCut = SafeMarkerBoundary(state.MarkerBuffer);
        var ready = state.MarkerBuffer[..safeCut];
        state.MarkerBuffer = state.MarkerBuffer[safeCut..];

        if (string.IsNullOrEmpty(ready))
        {
            return;
        }

        await BroadcastParsed(ready, characterId, ct).ConfigureAwait(false);
        state.FullContent += LlmMarkerParser.Parse(ready).CleanText;
    }

    private async Task FlushStateAsync(
        VoiceStreamState state, string? characterId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(state.MarkerBuffer))
        {
            return;
        }

        await BroadcastParsed(state.MarkerBuffer, characterId, ct).ConfigureAwait(false);
        state.FullContent += LlmMarkerParser.Parse(state.MarkerBuffer).CleanText;
        state.MarkerBuffer = string.Empty;
    }

    private async Task SynthesiseAsync(
        string fullContent, string? voice, string? characterId, CancellationToken ct)
    {
        if (_ttsProvider is null || string.IsNullOrWhiteSpace(fullContent))
        {
            return;
        }

        await foreach (var ttsChunk in _ttsProvider.SynthesizeAsync(fullContent, voice, ct))
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

    private async Task BroadcastParsed(string text, string? characterId, CancellationToken ct)
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
