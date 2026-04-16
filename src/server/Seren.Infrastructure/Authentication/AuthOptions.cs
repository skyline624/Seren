namespace Seren.Infrastructure.Authentication;

/// <summary>
/// JWT authentication options, bound from the <c>Auth</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>JWT signing key. In dev, read from UserSecrets; in prod, from env var or Key Vault.</summary>
    public string JwtSecret { get; set; } = string.Empty;

    /// <summary>Token issuer.</summary>
    public string Issuer { get; set; } = "seren.hub";

    /// <summary>Token audience.</summary>
    public string Audience { get; set; } = "seren.clients";

    /// <summary>Token expiration in minutes.</summary>
    public int TokenExpirationMinutes { get; set; } = 60;

    /// <summary>Whether authentication is required. When false, endpoints are open (dev mode).</summary>
    public bool RequireAuthentication { get; set; }
}
