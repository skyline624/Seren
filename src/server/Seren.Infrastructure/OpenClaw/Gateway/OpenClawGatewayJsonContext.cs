using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// AOT-safe <see cref="JsonSerializerContext"/> for the OpenClaw gateway
/// protocol. Kept separate from <c>Seren.Contracts.SerenJsonContext</c>
/// because these wire types are infrastructure-internal and must not leak
/// into the contract surface consumed by UI clients.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GatewayRequest))]
[JsonSerializable(typeof(GatewayResponse))]
[JsonSerializable(typeof(GatewayEvent))]
[JsonSerializable(typeof(ConnectParams))]
[JsonSerializable(typeof(ConnectClient))]
[JsonSerializable(typeof(ConnectAuth))]
[JsonSerializable(typeof(GatewayError))]
[JsonSerializable(typeof(HelloOkPayload))]
[JsonSerializable(typeof(HelloOkServer))]
[JsonSerializable(typeof(HelloOkFeatures))]
[JsonSerializable(typeof(HelloOkPolicy))]
[JsonSerializable(typeof(ConnectDevice))]
[JsonSerializable(typeof(ConnectChallengePayload))]
[JsonSerializable(typeof(ChatEventPayload))]
[JsonSerializable(typeof(ChatEventMessage))]
[JsonSerializable(typeof(ChatEventMessageContent))]
[JsonSerializable(typeof(ChatSendParams))]
[JsonSerializable(typeof(ChatSendResult))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class OpenClawGatewayJsonContext : JsonSerializerContext;
