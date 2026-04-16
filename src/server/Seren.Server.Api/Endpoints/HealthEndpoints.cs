using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Seren.Server.Api.Endpoints;

/// <summary>
/// Maps the <c>/health/live</c> and <c>/health/ready</c> endpoints.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>
    /// Liveness: the process is alive and can respond to HTTP.
    /// Readiness: the process has passed all readiness health checks.
    /// </summary>
    public static IEndpointRouteBuilder MapSerenHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = (ctx, _) =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return ctx.Response.WriteAsync("alive");
            },
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = reg => reg.Tags.Contains("ready"),
            ResponseWriter = (ctx, report) =>
            {
                ctx.Response.StatusCode = report.Status == HealthStatus.Healthy
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status503ServiceUnavailable;
                return ctx.Response.WriteAsync(report.Status.ToString());
            },
        });

        return endpoints;
    }
}
