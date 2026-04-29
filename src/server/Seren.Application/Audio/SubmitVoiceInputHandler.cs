using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.ValueObjects;

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

        // 1. Transcribe audio. The optional `command.SttEngine` lets the
        // user pin a specific local engine (Parakeet / Whisper-{size}),
        // and `command.SttLanguage` lets them force the decode language
        // — both flow inline in the WS payload. The router falls back
        // to the configured default when either is null or unknown.
        var transcription = await _sttProvider.TranscribeAsync(
            command.AudioData,
            command.Format,
            command.SttEngine,
            command.SttLanguage,
            cancellationToken)
            .ConfigureAwait(false);
        var text = transcription.Text;

        _logger.LogInformation(
            "Voice input transcribed: '{Text}' (length={Length}, language={Language}, confidence={Confidence}, audioBytes={AudioBytes}, format={Format})",
            text, text.Length, transcription.Language, transcription.Confidence,
            command.AudioData.Length, command.Format);

        // 1a. Empty transcription guard. Sending an empty user prompt to
        // OpenClaw is silently rejected by some upstream providers (e.g.
        // ollama/kimi-cloud) with a generic stream_error. Short-circuit
        // before the LLM call so the UI gets a clean "no speech captured"
        // signal instead of a hard failure popup.
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning(
                "Voice input STT returned empty text — skipping LLM call (audioBytes={AudioBytes}, format={Format}, confidence={Confidence}).",
                command.AudioData.Length, command.Format, transcription.Confidence);
            return string.Empty;
        }

        // 1a-bis. Echo the transcribed text back to the UI as the user
        // message so the chat panel reconciles its optimistic placeholder
        // with the actual transcription (or — for peers that didn't open
        // the optimistic bubble locally — renders the bubble fresh). The
        // text path emits OutputChatUser via SendTextMessageHandler;
        // here we mirror it. Reuse the client-minted id when present so
        // the originating tab merges instead of duplicating.
        var userMessageId = string.IsNullOrWhiteSpace(command.ClientMessageId)
            ? Guid.NewGuid().ToString("N")
            : command.ClientMessageId;
        await BroadcastUserEchoAsync(userMessageId, text, command.PeerId, cancellationToken)
            .ConfigureAwait(false);

        // 1b. Pre-warm the TTS engine for the detected language in the
        // background while the LLM stream runs. Cloud / no-op providers honour
        // the default no-op implementation, but local engines (VoxMind /
        // F5-TTS) load their language-specific ONNX sessions here so the cold-
        // load latency (~2-4 s) is masked behind the LLM window.
        // Fire-and-forget: failures are logged but don't fail the voice flow.
        _ = WarmUpTtsBackgroundAsync(transcription.Language, cancellationToken);

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
        //
        // Modality awareness — we DO NOT prepend a modality marker to the
        // user prompt: every model we tested (kimi-k2.6:cloud and qwen3.5)
        // narrated the marker back into the assistant turn ("L'utilisateur
        // a envoyé un message vocal qui dit…"), leaking reasoning into the
        // visible reply. The clean way to teach the assistant the message
        // came from voice is to add it to the active character's system
        // prompt (out of band, doesn't pollute the user turn) — tracked as
        // a follow-up. For now the LLM gets the raw transcription, identical
        // to the text path.
        var request = new ChatStreamRequest(
            SessionKey: sessionKey,
            UserText: text,
            PrimaryModel: effectiveAgentId,
            ClientMessageId: userMessageId,
            CharacterId: characterId,
            OnContent: (content, ct) => OnContentAsync(streamState, characterId, content, ct),
            OnTeardown: ct => FlushStateAsync(streamState, characterId, ct),
            OnSuccess: ct => SynthesiseAsync(streamState.FullContent, character?.Voice, transcription.Language, characterId, ct));

        await _pipeline.RunAsync(request, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Voice input stream completed for session {SessionKey}", sessionKey);

        return text;
    }

    private sealed class VoiceStreamState
    {
        /// <summary>True while the upstream LLM is inside a <c>&lt;think&gt;</c> /
        /// <c>&lt;action:think&gt;</c> block. Same role as the homonymous field
        /// in <c>SendTextMessageHandler.StreamState</c>.</summary>
        public bool IsThinking;

        /// <summary>Raw upstream text accumulated until a thinking transition
        /// is resolved. Hand-off to <see cref="MarkerBuffer"/> happens once the
        /// thinking filter returns visible text.</summary>
        public string TextBuffer = string.Empty;

        /// <summary>Visible text accumulated until a marker boundary is safe
        /// to broadcast. Holds only post-thinking content.</summary>
        public string MarkerBuffer = string.Empty;

        /// <summary>Cleaned visible content captured for post-stream TTS.</summary>
        public string FullContent = string.Empty;
    }

    private async Task BroadcastUserEchoAsync(
        string messageId, string text, string? originatingPeerId, CancellationToken ct)
    {
        // Note: unlike the text path, we DO NOT exclude the originating peer.
        // The voice flow's optimistic UI bubble carries a `🎙️ …` placeholder
        // (the originating tab does not yet know the transcription); the
        // server echo is the only signal that lets the originator replace
        // the placeholder with the actual transcribed text. Other peers
        // also see the echo and render the bubble fresh — both code paths
        // converge on the same `existing-by-id ? replace : insert` reducer
        // in the UI store.
        _ = originatingPeerId;

        var payload = new UserEchoPayload
        {
            MessageId = messageId,
            Text = text,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Attachments = null,
        };

        var envelope = CreateEnvelope(EventTypes.OutputChatUser, payload);
        await _hub.BroadcastAsync(envelope, excluding: null, ct).ConfigureAwait(false);
    }

    private async Task OnContentAsync(
        VoiceStreamState state, string? characterId, string content, CancellationToken ct)
    {
        // 1. Filter <think>/<action:think> reasoning blocks before any
        //    visible-content processing. Without this the voice path
        //    happily relays kimi-cloud's chain-of-thought into the chat
        //    bubble (the reasoning that the text path correctly hides).
        state.TextBuffer += content;
        var (visibleText, thinkingTransition) =
            LlmThinkingFilter.ExtractThinkingSegments(state.TextBuffer, ref state.IsThinking);
        state.TextBuffer = thinkingTransition.Remainder;

        foreach (var transition in thinkingTransition.Events)
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    transition ? EventTypes.OutputChatThinkingStart : EventTypes.OutputChatThinkingEnd,
                    new ChatEndPayload { CharacterId = characterId }),
                null, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(visibleText))
        {
            return;
        }

        // 2. Marker-boundary buffering on the visible-only stream so we
        //    never emit a half-formed `<emotion:` or `<action:wave` tag.
        state.MarkerBuffer += visibleText;
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
        // If the stream ended while the model was still inside an unclosed
        // thinking block, broadcast the missing thinking:end so the UI's
        // typing indicator can stop. Discard the leftover TextBuffer (it
        // is, by definition, reasoning content the user must never see).
        if (state.IsThinking)
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    EventTypes.OutputChatThinkingEnd,
                    new ChatEndPayload { CharacterId = characterId }),
                null, ct).ConfigureAwait(false);
            state.IsThinking = false;
            state.TextBuffer = string.Empty;
        }

        if (string.IsNullOrEmpty(state.MarkerBuffer))
        {
            return;
        }

        await BroadcastParsed(state.MarkerBuffer, characterId, ct).ConfigureAwait(false);
        state.FullContent += LlmMarkerParser.Parse(state.MarkerBuffer).CleanText;
        state.MarkerBuffer = string.Empty;
    }

    private async Task WarmUpTtsBackgroundAsync(string? language, CancellationToken ct)
    {
        if (_ttsProvider is null)
        {
            return;
        }

        try
        {
            await _ttsProvider.WarmUpAsync(language, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled — nothing to do.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "TTS warm-up failed for language={Language}; synthesis will cold-load on demand.",
                language);
        }
    }

    private async Task SynthesiseAsync(
        string fullContent, string? voice, string? language, string? characterId, CancellationToken ct)
    {
        if (_ttsProvider is null || string.IsNullOrWhiteSpace(fullContent))
        {
            return;
        }

        await foreach (var ttsChunk in _ttsProvider.SynthesizeAsync(fullContent, voice, language, ct))
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
