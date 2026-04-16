namespace Seren.Infrastructure.RateLimiting;

/// <summary>
/// Options for rate limiting, bound from the <c>Seren:RateLimit</c> section.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "Seren:RateLimit";

    /// <summary>Whether rate limiting is enabled. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum number of requests per window. Default: 100.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Window duration in seconds. Default: 10.</summary>
    public int WindowSeconds { get; set; } = 10;

    /// <summary>Number of segments per window (sliding window granularity). Default: 5.</summary>
    public int SegmentsPerWindow { get; set; } = 5;

    /// <summary>Maximum queue depth for queued requests. Default: 0 (no queue).</summary>
    public int QueueLimit { get; set; }
}
