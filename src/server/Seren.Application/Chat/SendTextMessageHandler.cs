using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;

namespace Seren.Application.Chat;

/// <summary>
/// Handles <see cref="SendTextMessageCommand"/> by issuing <c>chat.send</c>
/// on the OpenClaw gateway WebSocket, subscribing to the resulting stream,
/// and broadcasting chunks, emotion / action markers, and the stream-end
/// event to all connected peers via <see cref="ISerenHub"/>.
/// </summary>
/// <remarks>
/// System prompt management has moved upstream: each OpenClaw agent owns
/// its own persona + guardrails. Seren only forwards the user's current
/// turn and relies on the gateway to resolve the active agent from the
/// session context.
/// </remarks>
public sealed class SendTextMessageHandler : ICommandHandler<SendTextMessageCommand>
{
    private readonly IOpenClawChat _openClawChat;
    private readonly ICharacterRepository _characterRepository;
    private readonly ISerenHub _hub;
    private readonly IChatSessionKeyProvider _sessionKeyProvider;
    private readonly ILogger<SendTextMessageHandler> _logger;

    public SendTextMessageHandler(
        IOpenClawChat openClawChat,
        ICharacterRepository characterRepository,
        ISerenHub hub,
        IChatSessionKeyProvider sessionKeyProvider,
        ILogger<SendTextMessageHandler> logger)
    {
        _openClawChat = openClawChat;
        _characterRepository = characterRepository;
        _hub = hub;
        _sessionKeyProvider = sessionKeyProvider;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(SendTextMessageCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var character = await _characterRepository.GetActiveAsync(cancellationToken);
        // Single server-side session key: every connected device sees the
        // same conversation. SendTextMessageCommand.SessionId is ignored to
        // avoid clients accidentally forking their own private session.
        var sessionKey = _sessionKeyProvider.MainSessionKey;
        var characterId = character?.Id.ToString();

        // Model precedence is kept for logs + future per-call routing.
        // OpenClaw's chat.send RPC does not accept a per-request model
        // parameter and sessions.patch requires operator.admin (which we
        // don't hold), so the UI selection is applied via a separate
        // POST /api/models/apply endpoint that rewrites the gateway's
        // openclaw.json + restarts the process. Here we only log the
        // intent so the chat history stays attributable.
        var effectiveAgentId = request.Model ?? character?.AgentId;

        _logger.LogInformation(
            "Starting chat for session {SessionKey} (agentId={AgentId})",
            sessionKey, effectiveAgentId);

        var runId = await _openClawChat.StartAsync(
            sessionKey, request.Text, effectiveAgentId, cancellationToken).ConfigureAwait(false);

        // Streaming state machine — filters thinking segments and buffers
        // <emotion:*> / <action:*> markers that may straddle chunk boundaries.
        var isThinking = false;
        var textBuffer = string.Empty;
        var markerBuffer = string.Empty;

        await foreach (var delta in _openClawChat.SubscribeAsync(runId, cancellationToken))
        {
            if (string.IsNullOrEmpty(delta.Content))
            {
                if (delta.FinishReason is not null)
                {
                    break;
                }
                continue;
            }

            textBuffer += delta.Content;
            var (visibleText, thinkingTransition) = ExtractThinkingSegments(textBuffer, ref isThinking);
            textBuffer = thinkingTransition.Remainder;

            foreach (var transition in thinkingTransition.Events)
            {
                await _hub.BroadcastAsync(
                    CreateEnvelope(
                        transition ? EventTypes.OutputChatThinkingStart : EventTypes.OutputChatThinkingEnd,
                        new ChatEndPayload { CharacterId = characterId }),
                    null, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(visibleText))
            {
                continue;
            }

            markerBuffer += visibleText;
            var safeCut = SafeMarkerBoundary(markerBuffer);
            var readyForParse = markerBuffer[..safeCut];
            markerBuffer = markerBuffer[safeCut..];
            if (string.IsNullOrEmpty(readyForParse))
            {
                continue;
            }

            await BroadcastParsed(readyForParse, characterId, cancellationToken).ConfigureAwait(false);
        }

        // Flush the last buffered text (trailing characters after the last
        // marker, or a dangling "<" that never completed).
        if (!string.IsNullOrEmpty(markerBuffer))
        {
            await BroadcastParsed(markerBuffer, characterId, cancellationToken).ConfigureAwait(false);
        }

        if (isThinking)
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    EventTypes.OutputChatThinkingEnd,
                    new ChatEndPayload { CharacterId = characterId }),
                null, cancellationToken).ConfigureAwait(false);
        }

        await _hub.BroadcastAsync(
            CreateEnvelope(EventTypes.OutputChatEnd, new ChatEndPayload { CharacterId = characterId }),
            null, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Chat stream completed for session {SessionKey} (runId={RunId})", sessionKey, runId);
        return Unit.Value;
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

    /// <summary>
    /// Scans <paramref name="buffer"/> for <c>&lt;think&gt;</c> / <c>&lt;/think&gt;</c>
    /// tags that may be split across streamed chunks. Returns the visible
    /// portion ready to forward, the unconsumed suffix for the next chunk,
    /// and the transitions (true = entered, false = left).
    /// </summary>
    private static (string Visible, ThinkingTransition Transition) ExtractThinkingSegments(
        string buffer, ref bool isThinking)
    {
        var visible = new System.Text.StringBuilder();
        var transitions = new List<bool>();
        var index = 0;

        while (index < buffer.Length)
        {
            if (isThinking)
            {
                var close = buffer.IndexOf("</think>", index, StringComparison.Ordinal);
                if (close < 0)
                {
                    index = buffer.Length;
                    break;
                }
                index = close + "</think>".Length;
                isThinking = false;
                transitions.Add(false);
            }
            else
            {
                var open = buffer.IndexOf("<think>", index, StringComparison.Ordinal);
                if (open < 0)
                {
                    var safeEnd = SafeVisibleEnd(buffer, index);
                    visible.Append(buffer, index, safeEnd - index);
                    index = safeEnd;
                    break;
                }
                visible.Append(buffer, index, open - index);
                index = open + "<think>".Length;
                isThinking = true;
                transitions.Add(true);
            }
        }

        return (visible.ToString(), new ThinkingTransition(transitions, buffer[index..]));
    }

    private static int SafeVisibleEnd(string buffer, int start)
    {
        const string tag = "<think>";
        for (var prefixLen = Math.Min(tag.Length - 1, buffer.Length - start); prefixLen > 0; prefixLen--)
        {
            var candidateStart = buffer.Length - prefixLen;
            if (candidateStart < start)
            {
                break;
            }

            var span = buffer.AsSpan(candidateStart, prefixLen);
            if (span.SequenceEqual(tag.AsSpan(0, prefixLen)))
            {
                return candidateStart;
            }
        }
        return buffer.Length;
    }

    private sealed record ThinkingTransition(IReadOnlyList<bool> Events, string Remainder);

    private static int SafeMarkerBoundary(string buffer)
    {
        var lastOpen = buffer.LastIndexOf('<');
        if (lastOpen < 0)
        {
            return buffer.Length;
        }

        var matchingClose = buffer.IndexOf('>', lastOpen);
        return matchingClose >= 0 ? buffer.Length : lastOpen;
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
