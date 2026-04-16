using System.Collections.Concurrent;
using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Authentication;

/// <summary>
/// In-memory <see cref="ITokenRevocationStore"/> backed by a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>. Entries self-expire
/// via the background sweeper <see cref="TokenRevocationSweeper"/>, so
/// the dictionary never grows past the set of currently-valid revoked
/// tokens. Suitable for single-node deployments; a distributed cache
/// (Redis) would replace this in a multi-node scenario without touching
/// the abstraction.
/// </summary>
public sealed class InMemoryTokenRevocationStore : ITokenRevocationStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);

    internal int Count => _entries.Count;

    public ValueTask RevokeAsync(string jti, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jti);
        _entries[jti] = expiresAt;
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
        {
            return ValueTask.FromResult(false);
        }

        if (!_entries.TryGetValue(jti, out var expiresAt))
        {
            return ValueTask.FromResult(false);
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            _entries.TryRemove(jti, out _);
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(true);
    }

    /// <summary>
    /// Removes all entries whose expiration is before <paramref name="now"/>.
    /// Exposed to the sweeper so it can log how much it pruned.
    /// </summary>
    internal int PruneExpired(DateTimeOffset now)
    {
        var removed = 0;
        foreach (var (jti, expiresAt) in _entries)
        {
            if (expiresAt <= now && _entries.TryRemove(jti, out _))
            {
                removed++;
            }
        }
        return removed;
    }
}
