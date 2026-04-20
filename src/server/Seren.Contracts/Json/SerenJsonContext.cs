using System.Text.Json;
using System.Text.Json.Serialization;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;

namespace Seren.Contracts.Json;

/// <summary>
/// <see cref="JsonSerializerContext"/> used by the Seren hub for all wire
/// (de)serialization. Source-generated for AOT compatibility and to avoid
/// reflection at runtime.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WebSocketEnvelope))]
[JsonSerializable(typeof(EventMetadata))]
[JsonSerializable(typeof(EventIdentity))]
[JsonSerializable(typeof(ModuleIdentityDto))]
[JsonSerializable(typeof(AuthenticatePayload))]
[JsonSerializable(typeof(AuthenticatedPayload))]
[JsonSerializable(typeof(AnnouncePayload))]
[JsonSerializable(typeof(AnnouncedPayload))]
[JsonSerializable(typeof(HeartbeatPayload))]
[JsonSerializable(typeof(ErrorPayload))]
[JsonSerializable(typeof(ChatChunkPayload))]
[JsonSerializable(typeof(ChatEndPayload))]
[JsonSerializable(typeof(AvatarEmotionPayload))]
[JsonSerializable(typeof(AvatarActionPayload))]
[JsonSerializable(typeof(TextInputPayload))]
[JsonSerializable(typeof(VoiceInputPayload))]
[JsonSerializable(typeof(AudioPlaybackPayload))]
[JsonSerializable(typeof(LipsyncFramePayload))]
[JsonSerializable(typeof(SessionMessagePayload))]
[JsonSerializable(typeof(ApprovalRequestPayload))]
[JsonSerializable(typeof(ApprovalResolvedPayload))]
[JsonSerializable(typeof(AgentEventPayload))]
[JsonSerializable(typeof(ChatHistoryRequestPayload))]
[JsonSerializable(typeof(ChatHistoryBeginPayload))]
[JsonSerializable(typeof(ChatHistoryItemPayload))]
[JsonSerializable(typeof(ChatHistoryEndPayload))]
[JsonSerializable(typeof(ChatClearedPayload))]
public sealed partial class SerenJsonContext : JsonSerializerContext;
