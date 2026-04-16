using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Chat;

/// <summary>
/// Handles <see cref="SendTextMessageCommand"/> by streaming a chat completion
/// from OpenClaw Gateway and broadcasting chunks, emotion markers, and the
/// stream-end event to all connected peers via <see cref="ISerenHub"/>.
/// </summary>
public sealed class SendTextMessageHandler : ICommandHandler<SendTextMessageCommand>
{
    private readonly IOpenClawClient _openClawClient;
    private readonly ICharacterRepository _characterRepository;
    private readonly ISerenHub _hub;
    private readonly ILogger<SendTextMessageHandler> _logger;

    public SendTextMessageHandler(
        IOpenClawClient openClawClient,
        ICharacterRepository characterRepository,
        ISerenHub hub,
        ILogger<SendTextMessageHandler> logger)
    {
        _openClawClient = openClawClient;
        _characterRepository = characterRepository;
        _hub = hub;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(SendTextMessageCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var character = await _characterRepository.GetActiveAsync(cancellationToken);
        var messages = BuildMessages(request, character);

        var characterId = character?.Id.ToString();

        await foreach (var chunk in _openClawClient.StreamChatAsync(messages, character?.AgentId, cancellationToken))
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
        }

        var endEnvelope = CreateEnvelope(
            EventTypes.OutputChatEnd,
            new ChatEndPayload { CharacterId = characterId });

        await _hub.BroadcastAsync(endEnvelope, null, cancellationToken);

        _logger.LogInformation(
            "Chat stream completed for session {SessionId}",
            request.SessionId);

        return Unit.Value;
    }

    private static List<ChatMessage> BuildMessages(SendTextMessageCommand request, Domain.Entities.Character? character)
    {
        var messages = new List<ChatMessage>();

        if (character is { SystemPrompt.Length: > 0 })
        {
            messages.Add(new ChatMessage("system", character.SystemPrompt));
        }

        messages.Add(new ChatMessage("user", request.Text));

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
