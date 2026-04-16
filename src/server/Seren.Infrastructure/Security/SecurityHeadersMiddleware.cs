using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Seren.Infrastructure.Security;

/// <summary>
/// Middleware that adds enterprise-grade security headers to every response.
/// Applies before any endpoint executes so that even error responses carry
/// the protective headers. Configuration lives in <see cref="SecurityHeadersOptions"/>.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityHeadersOptions> options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);

        _next = next;
        _options = options.Value;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (_options.Enabled)
        {
            ApplyHeaders(context);
        }

        return _next(context);
    }

    private void ApplyHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Content-Security-Policy
        if (!string.IsNullOrWhiteSpace(_options.ContentSecurityPolicy)
            && !headers.ContainsKey("Content-Security-Policy"))
        {
            headers["Content-Security-Policy"] = _options.ContentSecurityPolicy;
        }

        // X-Content-Type-Options
        if (_options.NoSniff && !headers.ContainsKey("X-Content-Type-Options"))
        {
            headers["X-Content-Type-Options"] = "nosniff";
        }

        // X-Frame-Options
        if (!string.IsNullOrWhiteSpace(_options.XFrameOptions)
            && !headers.ContainsKey("X-Frame-Options"))
        {
            headers["X-Frame-Options"] = _options.XFrameOptions;
        }

        // Referrer-Policy
        if (!string.IsNullOrWhiteSpace(_options.ReferrerPolicy)
            && !headers.ContainsKey("Referrer-Policy"))
        {
            headers["Referrer-Policy"] = _options.ReferrerPolicy;
        }

        // Permissions-Policy
        if (!string.IsNullOrWhiteSpace(_options.PermissionsPolicy)
            && !headers.ContainsKey("Permissions-Policy"))
        {
            headers["Permissions-Policy"] = _options.PermissionsPolicy;
        }

        // Strict-Transport-Security (HTTPS only — harmful on plain HTTP)
        if (context.Request.IsHttps
            && !string.IsNullOrWhiteSpace(_options.StrictTransportSecurity)
            && !headers.ContainsKey("Strict-Transport-Security"))
        {
            headers["Strict-Transport-Security"] = _options.StrictTransportSecurity;
        }
    }
}

/// <summary>
/// DI and pipeline extensions for <see cref="SecurityHeadersMiddleware"/>.
/// </summary>
public static class SecurityHeadersExtensions
{
    public static IServiceCollection AddSerenSecurityHeaders(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<SecurityHeadersOptions>()
            .Bind(configuration.GetSection(SecurityHeadersOptions.SectionName));

        return services;
    }

    public static IApplicationBuilder UseSerenSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
