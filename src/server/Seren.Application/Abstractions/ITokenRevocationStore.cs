namespace Seren.Application.Abstractions;

/// <summary>
/// Tracks revoked JWT identifiers (<c>jti</c> claim). A token whose jti has
/// been stored here must be rejected by the authentication pipeline until
/// its natural expiration, at which point the entry can be swept away.
/// </summary>
public interface ITokenRevocationStore
{
    /// <summary>
    /// Marks the given <paramref name="jti"/> as revoked until
    /// <paramref name="expiresAt"/>. Idempotent: revoking twice is a no-op.
    /// </summary>
    ValueTask RevokeAsync(string jti, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> if the given <paramref name="jti"/> is currently
    /// revoked. Called on every authenticated request so implementations
    /// should be O(1) and avoid I/O.
    /// </summary>
    ValueTask<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default);
}
