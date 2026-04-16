using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// Configures the resilience pipeline for the OpenClaw HTTP client:
/// circuit breaker, retry with exponential backoff, and per-request timeout.
/// </summary>
public static class OpenClawResilienceHandler
{
    /// <summary>
    /// Name used to identify the resilience handler in the HTTP client pipeline.
    /// </summary>
    public const string HandlerName = "OpenClawResilience";

    /// <summary>
    /// Registers the standard resilience pipeline on the given
    /// <see cref="IHttpClientBuilder"/>: circuit breaker (5 failures in 30s
    /// opens for 30s), retry (3 attempts, exponential 2s/4s/8s), and total
    /// timeout of 30s per request.
    /// </summary>
    public static IHttpClientBuilder AddOpenClawResilience(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddResilienceHandler(HandlerName, static (builder, _) =>
        {
            // Circuit breaker: open after 5 failures within 30s, stay open for 30s.
            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
            });

            // Retry: 3 attempts with exponential backoff (2s, 4s, 8s).
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2),
            });

            // Total timeout per request attempt.
            builder.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(30),
            });
        });

        return builder;
    }
}
