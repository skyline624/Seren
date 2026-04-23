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
    [property: JsonPropertyName("idempotencyKey")] string IdempotencyKey,
    [property: JsonPropertyName("timeoutMs"),
        JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TimeoutMs = null);

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

/// <summary>
/// Parameters for the OpenClaw <c>sessions.patch</c> RPC, narrowed to the
/// two fields Seren cares about right now (the session key it is patching
/// and the model override to pin). Upstream's schema accepts many more
/// optional fields (<c>thinkingLevel</c>, <c>reasoningLevel</c>, …); they
/// can be added here as dedicated records if the UI ever exposes them.
/// </summary>
/// <remarks>
/// <paramref name="Model"/> is deliberately nullable: <c>null</c> clears a
/// previously-pinned override so the gateway falls back to the agent's
/// configured default on the next turn.
/// </remarks>
internal sealed record SessionsPatchModelParams(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("model"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Model);

/// <summary>
/// Parameters for the OpenClaw <c>chat.abort</c> RPC, mirrored from
/// <c>ChatAbortParamsSchema</c> upstream. Used by Seren's stop-button flow
/// and by the server-side idle/total-timeout safety net to free gateway
/// resources for a hung run.
/// </summary>
/// <remarks>
/// <paramref name="RunId"/> is optional upstream — when omitted the gateway
/// aborts the most recent run on the session. Seren always passes it
/// explicitly so a race between abort dispatch and a fresh turn cannot
/// cancel the wrong run.
/// </remarks>
internal sealed record ChatAbortParams(
    [property: JsonPropertyName("sessionKey")] string SessionKey,
    [property: JsonPropertyName("runId"),
        JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RunId);
