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

        // Stream chat — OpenClaw handles context via x-openclaw-session-key
        var fullContent = string.Empty;

        await foreach (var chunk in _openClawClient.StreamChatAsync(
            messages, character?.AgentId, sessionKey, cancellationToken))
        {
            if (string.IsNullOrEmpty(chunk.Content))
            {
                continue;
            }

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
