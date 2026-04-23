namespace Seren.Application.Abstractions;

/// <summary>
/// Tracks the currently-active chat run per session so that a client-initiated
/// abort (<c>input:chat:abort</c>) or a server-side idle/total timeout can
/// resolve the correct <c>runId</c> to cancel upstream even when the event
/// payload omits it.
/// </summary>
/// <remarks>
/// Seren uses a single shared session (<c>OpenClawOptions.MainSessionKey</c>),
/// so at most one run is active at a time; the registry is still keyed by
/// session to keep the abstraction multi-session-ready when that lands.
/// Implementations must be thread-safe — handlers register/unregister from
/// scoped request pipelines while the WebSocket processor may query the
/// registry from an unrelated frame.
/// </remarks>
public interface IChatRunRegistry
{
    /// <summary>Mark <paramref name="runId"/> as the active run for <paramref name="sessionKey"/>.</summary>
    void Register(string sessionKey, string runId);

    /// <summary>Clear <paramref name="runId"/> if it still holds the active slot.</summary>
    /// <remarks>
    /// Racing unregister with a newer Register must be a no-op: implementations
    /// compare the stored value and only clear when it matches <paramref name="runId"/>.
    /// </remarks>
    void Unregister(string sessionKey, string runId);

    /// <summary>Returns the active run id for <paramref name="sessionKey"/>, or <c>null</c>.</summary>
    string? GetActiveRun(string sessionKey);
}
