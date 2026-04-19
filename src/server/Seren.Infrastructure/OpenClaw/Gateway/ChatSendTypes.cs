using System.Text.Json.Serialization;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Parameters for the OpenClaw <c>chat.send</c> RPC, mirrored from
/// <c>src/gateway/protocol/schema/logs-chat.ts</c> (ChatSendParamsSchema).
/// </summary>
/// <remarks>
/// Upstream validates with <c>additionalProperties: false</c>, so only the
/// fields declared here will be accepted — adding untyped fields will
/// trigger <c>INVALID_REQUEST</c>.
/// <para/>
/// <paramref name="IdempotencyKey"/> is <b>required</b> upstream and drives the
/// returned run identifier (sending twice with the same key returns
/// <c>status:"in_flight"</c> instead of creating a new run). Seren uses a
/// fresh GUID per call to avoid cross-request collisions.
/// </remarks>
internal sealed record ChatSendParams(
    [property: JsonPropertyName("sessionKey")] string SessionKey,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("idempotencyKey")] string IdempotencyKey);

/// <summary>
/// Successful response payload returned by <c>chat.send</c>: the gateway
/// acknowledges the submission and starts streaming <c>"chat"</c> events
/// correlated by <see cref="RunId"/>.
/// </summary>
/// <remarks>
/// <see cref="Status"/> is <c>"started"</c> for a fresh run or
/// <c>"in_flight"</c> when the same <c>idempotencyKey</c> was already in
/// progress — in both cases the caller should subscribe to the stream using
/// the returned <see cref="RunId"/>.
/// </remarks>
internal sealed record ChatSendResult(
    [property: JsonPropertyName("runId")] string RunId,
    [property: JsonPropertyName("status")] string Status);
