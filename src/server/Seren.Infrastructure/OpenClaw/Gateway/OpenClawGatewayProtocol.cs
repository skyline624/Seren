using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// OpenClaw gateway WebSocket protocol records, mirrored from the reference
/// schema at <c>src/gateway/protocol/schema/frames.ts</c> (openclaw/openclaw).
/// </summary>
/// <remarks>
/// Only the fields Seren needs for a healthy session are modelled. Unknown
/// fields are tolerated on read (camel-case property naming, non-strict
/// deserialization); nulls are omitted on write so we stay compatible with
/// the gateway's strict validators.
/// </remarks>
internal static class OpenClawGatewayProtocol
{
    /// <summary>Highest protocol version Seren speaks.</summary>
    public const int ProtocolVersion = 3;

    /// <summary>Client identifier registered in OpenClaw's <c>GATEWAY_CLIENT_IDS</c> enum.</summary>
    public const string ClientId = "gateway-client";

    /// <summary>Client mode — Seren runs as a backend, not a UI.</summary>
    public const string ClientMode = "backend";

    /// <summary>Frame <c>type</c> discriminator: outbound request.</summary>
    public const string FrameTypeRequest = "req";

    /// <summary>Frame <c>type</c> discriminator: inbound response.</summary>
    public const string FrameTypeResponse = "res";

    /// <summary>Frame <c>type</c> discriminator: inbound server-push event.</summary>
    public const string FrameTypeEvent = "event";

    /// <summary>Event name for the server's tick heartbeat.</summary>
    public const string TickEventName = "tick";

    /// <summary>
    /// Pre-handshake event sent by the gateway; carries a nonce used only by
    /// device-signature auth. With shared-secret bearer auth (our case) the
    /// nonce is ignored.
    /// </summary>
    public const string ConnectChallengeEventName = "connect.challenge";

    /// <summary>Gateway RPC method name for the handshake.</summary>
    public const string ConnectMethodName = "connect";

    /// <summary>
    /// Scopes a Seren backend self-declares at handshake time. The gateway
    /// keeps these values for shared-token backend clients; they grant
    /// access to <c>chat.send</c> (needs <c>operator.write</c>) and
    /// <c>models.list</c> (needs <c>operator.read</c>, implied by write).
    /// </summary>
    public static readonly IReadOnlyList<string> BackendOperatorScopes =
        new[] { "operator.write" };
}

/// <summary>
/// Outbound request frame: <c>{type:"req", id, method, params?}</c>.
/// </summary>
internal sealed record GatewayRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params)
{
    // Discriminator literal required by the gateway schema. `init` keeps
    // the value effectively const but still serializable via source-gen.
    [JsonPropertyName("type")]
    public string Type { get; init; } = OpenClawGatewayProtocol.FrameTypeRequest;
}

/// <summary>
/// Inbound response frame: <c>{type:"res", id, ok, payload?, error?}</c>.
/// </summary>
internal sealed record GatewayResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("payload")] JsonElement? Payload,
    [property: JsonPropertyName("error")] GatewayError? Error);

/// <summary>
/// Error payload attached to a failed <see cref="GatewayResponse"/>.
/// </summary>
internal sealed record GatewayError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] JsonElement? Details,
    [property: JsonPropertyName("retryable")] bool? Retryable,
    [property: JsonPropertyName("retryAfterMs")] int? RetryAfterMs);

/// <summary>
/// Inbound event frame: <c>{type:"event", event, payload?, seq?, stateVersion?}</c>.
/// </summary>
/// <remarks>
/// <paramref name="StateVersion"/> is kept as <see cref="JsonElement"/> rather than
/// typed because OpenClaw ships it as an object (<c>{presence, health}</c>) whose
/// semantics are not yet meaningful to Seren — we only need to relay it forward
/// should downstream handlers start caring.
/// </remarks>
internal sealed record GatewayEvent(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("payload")] JsonElement? Payload,
    [property: JsonPropertyName("seq")] long? Seq,
    [property: JsonPropertyName("stateVersion")] JsonElement? StateVersion);

/// <summary>
/// Body of the <c>connect</c> request — the handshake payload sent once per
/// connection, matches <c>ConnectParamsSchema</c> upstream.
/// </summary>
/// <remarks>
/// <paramref name="Scopes"/> is self-declared by the client: the gateway keeps
/// these values when the connection is authenticated via a shared token and
/// the client runs in <c>backend</c> mode (our case). A backend like Seren
/// needs <c>operator.write</c> to call <c>chat.send</c> and <c>models.list</c>;
/// the hierarchy resolver treats <c>operator.write</c> as implying read, so
/// a single scope covers both.
/// </remarks>
internal sealed record ConnectParams(
    [property: JsonPropertyName("minProtocol")] int MinProtocol,
    [property: JsonPropertyName("maxProtocol")] int MaxProtocol,
    [property: JsonPropertyName("client")] ConnectClient Client,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("scopes")] IReadOnlyList<string>? Scopes,
    [property: JsonPropertyName("device")] ConnectDevice? Device,
    [property: JsonPropertyName("auth")] ConnectAuth? Auth);

/// <summary>Client-identity section of <see cref="ConnectParams"/>.</summary>
internal sealed record ConnectClient(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("instanceId")] string? InstanceId);

/// <summary>
/// Ed25519 identity block — mandatory for the gateway to preserve the
/// <see cref="ConnectParams.Scopes"/> we self-declare. Without this block,
/// <c>connect-policy.ts#shouldClearUnboundScopesForMissingDeviceIdentity</c>
/// strips the scopes after auth and any RPC requiring a scope (chat.send,
/// models.list, …) fails with <c>missing scope: operator.write</c>.
/// </summary>
/// <remarks>
/// Field formats:
/// <list type="bullet">
///  <item><see cref="Id"/>: hex lowercase SHA-256 of the raw public key (64 chars).</item>
///  <item><see cref="PublicKey"/>: base64url of the 32-byte Ed25519 raw public key.</item>
///  <item><see cref="Signature"/>: base64url of the 64-byte Ed25519 signature.</item>
///  <item><see cref="SignedAt"/>: Unix epoch ms; gateway allows ±2 min skew.</item>
///  <item><see cref="Nonce"/>: value received in the pre-handshake <c>connect.challenge</c> event.</item>
/// </list>
/// </remarks>
internal sealed record ConnectDevice(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("publicKey")] string PublicKey,
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("signedAt")] long SignedAt,
    [property: JsonPropertyName("nonce")] string Nonce);

/// <summary>
/// Shared-secret auth section. The bearer token is also present in the HTTP
/// Authorization header on upgrade — sending it here covers the frame-level
/// auth channel expected by OpenClaw.
/// <para/>
/// <see cref="BootstrapToken"/> is only used on the very first boot, to
/// auto-approve the new device identity against OpenClaw's pairing store
/// (see <c>device-bootstrap.ts</c>). After the device is paired, future
/// handshakes can send <c>null</c> there.
/// </summary>
internal sealed record ConnectAuth(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("bootstrapToken")] string? BootstrapToken = null);

/// <summary>
/// Payload carried by the pre-handshake <c>connect.challenge</c> event —
/// contains the nonce we must fold into the signed V3 auth payload.
/// </summary>
internal sealed record ConnectChallengePayload(
    [property: JsonPropertyName("nonce")] string Nonce);

/// <summary>
/// Payload of the successful handshake response (<c>hello-ok</c>).
/// </summary>
internal sealed record HelloOkPayload(
    [property: JsonPropertyName("protocol")] int Protocol,
    [property: JsonPropertyName("server")] HelloOkServer Server,
    [property: JsonPropertyName("features")] HelloOkFeatures Features,
    [property: JsonPropertyName("policy")] HelloOkPolicy Policy,
    [property: JsonPropertyName("canvasHostUrl")] string? CanvasHostUrl);

internal sealed record HelloOkServer(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("connId")] string ConnId);

internal sealed record HelloOkFeatures(
    [property: JsonPropertyName("methods")] IReadOnlyList<string> Methods,
    [property: JsonPropertyName("events")] IReadOnlyList<string> Events);

internal sealed record HelloOkPolicy(
    [property: JsonPropertyName("maxPayload")] int MaxPayload,
    [property: JsonPropertyName("maxBufferedBytes")] int MaxBufferedBytes,
    [property: JsonPropertyName("tickIntervalMs")] int TickIntervalMs);
