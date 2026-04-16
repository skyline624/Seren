using System.Security.Claims;

namespace Seren.Application.Abstractions;

/// <summary>
/// Application-layer contract for JWT token generation and validation.
/// Implemented by <c>Seren.Infrastructure.Authentication.JwtTokenService</c> (DIP).
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a JWT for the given peer and role.
    /// </summary>
    /// <param name="peerId">Unique peer identifier (becomes the <c>sub</c> claim).</param>
    /// <param name="role">Peer role (becomes the <c>role</c> claim).</param>
    /// <returns>The encoded JWT string.</returns>
    string GenerateToken(Guid peerId, string role);

    /// <summary>
    /// Validates a JWT and returns the principal, or <c>null</c> if invalid.
    /// </summary>
    /// <param name="token">The encoded JWT string.</param>
    /// <returns>The <see cref="ClaimsPrincipal"/>, or <c>null</c> if the token is invalid or expired.</returns>
    ClaimsPrincipal? ValidateToken(string token);

    /// <summary>
    /// Revokes a token by its <c>jti</c> claim for the remaining lifetime
    /// derived from the token's <c>exp</c> claim. After this call, any
    /// authenticated request with the same token will be rejected by the
    /// <see cref="ITokenRevocationStore"/>.
    /// </summary>
    /// <param name="token">The encoded JWT string to revoke.</param>
    /// <returns><c>true</c> if the token was parsed and scheduled for revocation.</returns>
    ValueTask<bool> RevokeAsync(string token, CancellationToken cancellationToken = default);
}
