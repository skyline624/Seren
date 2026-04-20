namespace Seren.Application.Abstractions;

/// <summary>
/// Application-layer abstraction for reading the persisted chat transcript
/// from OpenClaw and for triggering an in-place session reset (which clears
/// the LLM context without disturbing the long-term memory plugins, the
/// device pairing or the session key itself).
/// </summary>
/// <remarks>
/// Implemented by the infrastructure layer on top of
/// <c>IOpenClawGateway.CallAsync("chat.history" | "sessions.reset", …)</c>.
/// Modelling these as an Application contract keeps handlers free of
/// gateway plumbing and makes them trivially unit-testable.
/// </remarks>
public interface IOpenClawHistory
{
    /// <summary>
    /// Read the current persisted transcript for the configured main session.
    /// Messages are returned in chronological order (oldest first).
    /// </summary>
    /// <param name="limit">Maximum number of messages to return (server caps applied).</param>
    /// <param name="cancellationToken">Cancellation propagated to the underlying RPC.</param>
    Task<IReadOnlyList<ChatHistoryMessage>> LoadAsync(
        int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Archive the active transcript and start a fresh one for the same
    /// session key. Long-term memory plugins, paired devices, and the session
    /// key itself are unaffected.
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One persisted message returned by <see cref="IOpenClawHistory.LoadAsync"/>.
/// </summary>
/// <param name="MessageId">Stable identifier (run id for assistant turns, content-hash + ts for user turns).</param>
/// <param name="Role">"user", "assistant", or "system".</param>
/// <param name="Content">Plain text with markers already stripped.</param>
/// <param name="Timestamp">Unix epoch milliseconds when the message was originally produced.</param>
/// <param name="Emotion">Optional emotion extracted from upstream markers.</param>
public sealed record ChatHistoryMessage(
    string MessageId,
    string Role,
    string Content,
    long Timestamp,
    string? Emotion);
