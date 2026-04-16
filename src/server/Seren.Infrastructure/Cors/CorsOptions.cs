namespace Seren.Infrastructure.Cors;

/// <summary>
/// Options for CORS, bound from the <c>Seren:Cors</c> section.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Seren:Cors";

    /// <summary>CORS policy name.</summary>
    public const string PolicyName = "SerenCorsPolicy";

    /// <summary>Allowed origins. Empty means no CORS (same-origin only).</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Allowed HTTP methods.</summary>
    public string[] AllowedMethods { get; set; } = ["GET", "POST", "PUT", "DELETE", "OPTIONS"];

    /// <summary>Allowed headers.</summary>
    public string[] AllowedHeaders { get; set; } = ["Content-Type", "Authorization", "X-Requested-With"];

    /// <summary>Whether to allow credentials (cookies, auth headers).</summary>
    public bool AllowCredentials { get; set; } = true;
}
