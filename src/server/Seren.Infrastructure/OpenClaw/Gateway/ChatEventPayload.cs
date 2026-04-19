using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Shape of the OpenClaw <c>"chat"</c> event payload, mirrored from
/// <c>src/gateway/server-chat.ts</c> emitChatDelta / emitChatFinal.
/// </summary>
/// <remarks>
/// OpenClaw emits the assistant text <b>cumulatively</b>: each delta contains
/// the full text buffered so far, not the new fragment. Callers that need an
/// incremental delta must diff against the previous value themselves.
/// Deltas are additionally throttled at 150 ms server-side.
/// </remarks>
internal sealed record ChatEventPayload(
    [property: JsonPropertyName("runId")] string RunId,
    [property: JsonPropertyName("sessionKey")] string? SessionKey,
    [property: JsonPropertyName("seq")] long? Seq,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("message")] ChatEventMessage? Message,
    [property: JsonPropertyName("stopReason")] string? StopReason,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("errorKind")] string? ErrorKind);

/// <summary>Message body inside a <see cref="ChatEventPayload"/>.</summary>
internal sealed record ChatEventMessage(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] IReadOnlyList<ChatEventMessageContent>? Content,
    [property: JsonPropertyName("timestamp")] long? Timestamp);

/// <summary>A single entry inside <see cref="ChatEventMessage.Content"/>.</summary>
internal sealed record ChatEventMessageContent(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("text")] string? Text);

/// <summary>
/// Canonical string values for <see cref="ChatEventPayload.State"/>.
/// </summary>
internal static class ChatEventState
{
    public const string Delta = "delta";
    public const string Final = "final";
    public const string Aborted = "aborted";
    public const string Error = "error";

    public static bool IsTerminal(string state) =>
        state is Final or Aborted or Error;
}
