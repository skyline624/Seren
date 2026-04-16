using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.Entities;

namespace Seren.Application.Chat;

/// <summary>
/// Handles <see cref="SendTextMessageCommand"/> by streaming a chat completion
/// from OpenClaw Gateway and broadcasting chunks, emotion markers, and the
/// stream-end event to all connected peers via <see cref="ISerenHub"/>.
/// Conversation history is managed server-side by OpenClaw via session keys.
/// Messages are persisted locally for UI display purposes.
/// </summary>
public sealed class SendTextMessageHandler : ICommandHandler<SendTextMessageCommand>
{
    private readonly IOpenClawClient _openClawClient;
    private readonly ICharacterRepository _characterRepository;
    private readonly IConversationRepository _conversationRepository;
    private readonly ISerenHub _hub;
    private readonly ILogger<SendTextMessageHandler> _logger;

    public SendTextMessageHandler(
        IOpenClawClient openClawClient,
        ICharacterRepository characterRepository,
        IConversationRepository conversationRepository,
        ISerenHub hub,
        ILogger<SendTextMessageHandler> logger)
    {
        _openClawClient = openClawClient;
        _characterRepository = characterRepository;
        _conversationRepository = conversationRepository;
        _hub = hub;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(SendTextMessageCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var character = await _characterRepository.GetActiveAsync(cancellationToken);
        var sessionId = request.SessionId ?? Guid.NewGuid();
        var sessionKey = sessionId.ToString("N");

        // Build messages with only the current turn — OpenClaw maintains
        // conversation history server-side via the session key.
        var messages = BuildMessages(request.Text, character);
        var characterId = character?.Id.ToString();

        // Persist user message locally for UI history
        var userMessage = ConversationMessage.Create(sessionId, "user", request.Text, character?.Id);
        await _conversationRepository.AddAsync(userMessage, cancellationToken);

        // Stream chat — OpenClaw handles context via x-openclaw-session-key.
        // A small state machine filters the model's chain-of-thought (either
        // surfaced via the `reasoning` field or wrapped in `<think>…</think>`
        // tags inside regular content) so it never reaches the UI as answer
        // text: instead, we emit thinking:start / thinking:end markers so the
        // client can show an animated indicator while the model is thinking.
        var fullContent = string.Empty;
        var isThinking = false;
        var textBuffer = string.Empty;

        await foreach (var chunk in _openClawClient.StreamChatAsync(
            messages, character?.AgentId, sessionKey, cancellationToken))
        {
            if (string.IsNullOrEmpty(chunk.Content))
            {
                continue;
            }

            // Case 1: provider surfaces reasoning on a dedicated field.
            if (chunk.IsReasoning)
            {
                if (!isThinking)
                {
                    isThinking = true;
                    await _hub.BroadcastAsync(
                        CreateEnvelope(
                            EventTypes.OutputChatThinkingStart,
                            new ChatEndPayload { CharacterId = characterId }),
                        null,
                        cancellationToken)
                        .ConfigureAwait(false);
                }
                continue;
            }

            // Transition from reasoning field → answer content means the model
            // just finished thinking. Close the indicator before broadcasting.
            if (isThinking)
            {
                isThinking = false;
                await _hub.BroadcastAsync(
                    CreateEnvelope(
                        EventTypes.OutputChatThinkingEnd,
                        new ChatEndPayload { CharacterId = characterId }),
                    null,
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            // Case 2: reasoning is embedded inline as <think>…</think>.
            textBuffer += chunk.Content;
            var (visibleText, thinkingTransition) = ExtractThinkingSegments(textBuffer, ref isThinking);
            textBuffer = thinkingTransition.Remainder;

            foreach (var transition in thinkingTransition.Events)
            {
                await _hub.BroadcastAsync(
                    CreateEnvelope(
                        transition ? EventTypes.OutputChatThinkingStart : EventTypes.OutputChatThinkingEnd,
                        new ChatEndPayload { CharacterId = characterId }),
                    null,
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(visibleText))
            {
                continue;
            }

            var parseResult = LlmMarkerParser.Parse(visibleText);

            if (!string.IsNullOrEmpty(parseResult.CleanText))
            {
                await _hub.BroadcastAsync(
                    CreateEnvelope(
                        EventTypes.OutputChatChunk,
                        new ChatChunkPayload { Content = parseResult.CleanText, CharacterId = characterId }),
                    null,
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var emotion in parseResult.Emotions)
            {
                await _hub.BroadcastAsync(
                    CreateEnvelope(
                        EventTypes.AvatarEmotion,
                        new AvatarEmotionPayload { Emotion = emotion.Emotion, CharacterId = characterId }),
                    null,
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            fullContent += parseResult.CleanText;
        }

        // Graceful close of a dangling thinking state (unclosed <think> tag).
        if (isThinking)
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    EventTypes.OutputChatThinkingEnd,
                    new ChatEndPayload { CharacterId = characterId }),
                null,
                cancellationToken)
                .ConfigureAwait(false);
        }

        var endEnvelope = CreateEnvelope(
            EventTypes.OutputChatEnd,
            new ChatEndPayload { CharacterId = characterId });

        await _hub.BroadcastAsync(endEnvelope, null, cancellationToken);

        // Persist assistant response locally for UI history
        if (!string.IsNullOrWhiteSpace(fullContent))
        {
            var assistantMessage = ConversationMessage.Create(sessionId, "assistant", fullContent, character?.Id);
            await _conversationRepository.AddAsync(assistantMessage, cancellationToken);
        }

        _logger.LogInformation(
            "Chat stream completed for session {SessionId}",
            sessionId);

        return Unit.Value;
    }

    /// <summary>
    /// Scans <paramref name="buffer"/> for <c>&lt;think&gt;</c> / <c>&lt;/think&gt;</c>
    /// tags that may be split across multiple streamed chunks. Returns the
    /// visible (non-thinking) portion ready to be forwarded to the UI, the
    /// unconsumed suffix to be prepended to the next chunk, and any
    /// thinking-state transitions that occurred (true = entered thinking,
    /// false = left thinking).
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
                // Look for the closing tag; keep the buffer until it is available.
                var close = buffer.IndexOf("</think>", index, StringComparison.Ordinal);
                if (close < 0)
                {
                    // Entire remainder is thinking. Discard — thinking content
                    // never reaches the assistant bubble.
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
                    // Last fragment might be a partial "<think>" prefix — hold it
                    // back so the next chunk can complete the tag.
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

    /// <summary>
    /// Finds the last character index in <paramref name="buffer"/> that can
    /// be safely emitted without swallowing the start of a partial
    /// <c>&lt;think&gt;</c> tag (e.g. a chunk ending in <c>"&lt;thi"</c>).
    /// </summary>
    private static int SafeVisibleEnd(string buffer, int start)
    {
        const string tag = "<think>";
        // Scan backwards from the end for any prefix of the opening tag.
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

    private static List<ChatMessage> BuildMessages(string userText, Character? character)
    {
        var messages = new List<ChatMessage>();

        if (character is { SystemPrompt.Length: > 0 })
        {
            messages.Add(new ChatMessage("system", character.SystemPrompt));
        }

        messages.Add(new ChatMessage("user", userText));

        return messages;
    }

    private static WebSocketEnvelope CreateEnvelope(string eventType, object payload)
    {
        // Serialize with camelCase to match the wire contract the SDK expects
        // (e.g. `content`, `characterId` — not `Content`/`CharacterId`).
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
