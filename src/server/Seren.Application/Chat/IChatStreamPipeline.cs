namespace Seren.Application.Chat;

/// <summary>
/// Single orchestration point for streaming a chat completion from OpenClaw:
/// start the run, subscribe to chunks, apply idle/total timeouts, retry or
/// cascade through fallback providers when the stream stalls before
/// delivering content, and emit the standard terminal broadcasts
/// (<c>output:chat:end</c>, optional <c>error</c>, optional
/// <c>output:chat:provider-degraded</c>) on the hub.
/// </summary>
/// <remarks>
/// Consumed by both <see cref="SendTextMessageHandler"/> and the voice
/// handler. Each caller injects its own domain logic via the
/// <see cref="ChatStreamRequest.OnContent"/> callback (marker parsing,
/// chunk broadcasting), keeping the pipeline transport-agnostic and
/// easily testable in isolation.
/// </remarks>
public interface IChatStreamPipeline
{
    Task<ChatStreamOutcome> RunAsync(ChatStreamRequest request, CancellationToken cancellationToken);
}

/// <summary>Inputs for a single chat-stream run.</summary>
/// <param name="SessionKey">OpenClaw session to stream against.</param>
/// <param name="UserText">The user message to send upstream.</param>
/// <param name="PrimaryModel">Optional primary model id; null lets the gateway
/// pick the agent's default.</param>
/// <param name="ClientMessageId">Optional client-minted id used as the first
/// attempt's idempotency key (so the multi-tab echo stays coherent). Retries
/// mint fresh GUIDs regardless of this value.</param>
/// <param name="CharacterId">Currently active character id; passed through
/// to the hub envelopes so UIs can route events per-character.</param>
/// <param name="OnContent">Invoked for each non-empty content chunk streamed
/// from OpenClaw. The caller is responsible for parsing + broadcasting. When
/// this callback returns, the pipeline considers content delivered and
/// disables further retries.</param>
/// <param name="OnTeardown">Optional, always invoked immediately before the
/// pipeline emits its terminal <c>output:chat:end</c>, regardless of outcome.
/// Use for handler-specific cleanup (flush buffered text, close an open
/// thinking state, etc.). Failures are swallowed so they can't block
/// teardown.</param>
/// <param name="OnSuccess">Optional, invoked once on successful stream end
/// (a <c>FinishReason</c> arrived). Not called on timeout/error. Use for
/// post-stream actions that only make sense on a complete answer (TTS
/// synthesis, memory commit, etc.).</param>
public sealed record ChatStreamRequest(
    string SessionKey,
    string UserText,
    string? PrimaryModel,
    string? ClientMessageId,
    string? CharacterId,
    Func<string, CancellationToken, Task> OnContent,
    Func<CancellationToken, Task>? OnTeardown = null,
    Func<CancellationToken, Task>? OnSuccess = null);

/// <summary>Result of a single chat-stream run.</summary>
/// <param name="RunId">Upstream run id of the <i>last</i> attempt (the one that
/// produced the final outcome).</param>
/// <param name="ModelUsed">Provider/model id of the last attempt.</param>
/// <param name="AttemptsMade">How many attempts were tried; 1 if no retry/fallback
/// was needed, 2+ when the resilience policy engaged.</param>
/// <param name="Outcome">Final outcome — one of the <see cref="ChatStreamOutcomes"/>
/// string constants.</param>
public sealed record ChatStreamOutcome(
    string RunId,
    string ModelUsed,
    int AttemptsMade,
    string Outcome);

/// <summary>Canonical outcome strings used by <see cref="ChatStreamOutcome.Outcome"/>.</summary>
public static class ChatStreamOutcomes
{
    public const string Ok = "ok";
    public const string IdleTimeout = "idle_timeout";
    public const string TotalTimeout = "total_timeout";
    public const string UserAbort = "user_abort";
    public const string Error = "error";
}
