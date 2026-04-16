using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Seren.Infrastructure.RateLimiting;

/// <summary>
/// Extension methods to configure rate limiting for Seren.
/// Uses a sliding window algorithm with per-peer partitioning.
/// </summary>
public static class RateLimitingServiceExtensions
{
    /// <summary>
    /// Adds rate limiting services and configures a sliding window
    /// per-peer rate limiter. When <c>RateLimitOptions.Enabled</c> is false,
    /// rate limiting is disabled (useful for development).
    /// </summary>
    public static IServiceCollection AddSerenRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateOnStart();

        var rateLimitOptions = new RateLimitOptions();
        configuration.GetSection(RateLimitOptions.SectionName).Bind(rateLimitOptions);

        if (rateLimitOptions.Enabled)
        {
            services.AddRateLimiter(limiterOptions =>
            {
                limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                limiterOptions.AddSlidingWindowLimiter(policyName: "sliding", opt =>
                {
                    opt.PermitLimit = rateLimitOptions.PermitLimit;
                    opt.Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds);
                    opt.SegmentsPerWindow = rateLimitOptions.SegmentsPerWindow;
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = rateLimitOptions.QueueLimit;
                });
            });
        }

        return services;
    }

    /// <summary>
    /// Adds the rate limiting middleware. Call this after <c>AddSerenRateLimiting</c>.
    /// When disabled, this is a no-op.
    /// </summary>
    public static IApplicationBuilder UseSerenRateLimiting(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices
            .GetRequiredService<IOptions<RateLimitOptions>>().Value;

        if (!options.Enabled)
        {
            return app;
        }

        app.UseRateLimiter();

        return app;
    }
}
