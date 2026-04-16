using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Authentication;

/// <summary>
/// JWT token service using <c>System.IdentityModel.Tokens.Jwt</c>.
/// Signs tokens with a symmetric HMAC-SHA256 key derived from <see cref="AuthOptions.JwtSecret"/>.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly AuthOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;
    private readonly ITokenRevocationStore _revocationStore;

    public JwtTokenService(IOptions<AuthOptions> options, ITokenRevocationStore revocationStore)
    {
        _options = options.Value;
        _revocationStore = revocationStore;

        // In dev mode (empty secret), use a placeholder key so DI construction
        // succeeds. Tokens generated with this key are not secure — authentication
        // must be disabled (RequireAuthentication=false).
        var secret = string.IsNullOrWhiteSpace(_options.JwtSecret)
            ? "dev-placeholder-key-not-for-production-use-change-me"
            : _options.JwtSecret;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    /// <inheritdoc />
    public string GenerateToken(Guid peerId, string role)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.TokenExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, peerId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, role),
            new("peerId", peerId.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc />
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _validationParameters, out _);
            return principal;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            return false;
        }

        JwtSecurityToken parsed;
        try
        {
            parsed = handler.ReadJwtToken(token);
        }
        catch
        {
            return false;
        }

        var jti = parsed.Id;
        if (string.IsNullOrWhiteSpace(jti))
        {
            return false;
        }

        // Store until the token would naturally expire; after that the sweeper
        // removes the entry automatically.
        var expiresAt = parsed.ValidTo == default
            ? DateTimeOffset.UtcNow.AddMinutes(_options.TokenExpirationMinutes)
            : new DateTimeOffset(parsed.ValidTo, TimeSpan.Zero);

        await _revocationStore.RevokeAsync(jti, expiresAt, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
