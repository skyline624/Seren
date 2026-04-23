using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Realtime;

/// <summary>
/// Thread-safe, in-memory implementation of <see cref="IChatRunRegistry"/>.
/// Backed by a <see cref="ConcurrentDictionary{TKey, TValue}"/> keyed by
/// session key. Registered as a singleton so all scoped Mediator handlers
/// share the same view of which run is currently streaming.
/// </summary>
/// <remarks>
/// When Seren scales beyond a single process a distributed registry
/// (Redis, Orleans grain…) will replace this — the abstraction stays stable.
/// </remarks>
public sealed class InMemoryChatRunRegistry : IChatRunRegistry
{
    private readonly ConcurrentDictionary<string, string> _activeRuns = new(StringComparer.Ordinal);

    public void Register(string sessionKey, string runId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionKey);
        ArgumentException.ThrowIfNullOrEmpty(runId);

        _activeRuns[sessionKey] = runId;
    }

    public void Unregister(string sessionKey, string runId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionKey);
        ArgumentException.ThrowIfNullOrEmpty(runId);

        // Only clear when the slot still holds THIS run; a newer turn may
        // have started and overwritten the entry before the previous
        // handler's finally block ran.
        _activeRuns.TryRemove(new KeyValuePair<string, string>(sessionKey, runId));
    }

    public string? GetActiveRun(string sessionKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionKey);
        return _activeRuns.TryGetValue(sessionKey, out var runId) ? runId : null;
    }
}
