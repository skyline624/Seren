using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace Seren.Server.Api.IntegrationTests;

/// <summary>
/// Verifies that every HTTP response carries the security headers defined in
/// <c>Seren.Infrastructure.Security.SecurityHeadersMiddleware</c>. Uses the
/// <c>/</c> endpoint (Phase 1 landing text) as a minimal-overhead probe.
/// </summary>
public sealed class SecurityHeadersTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SecurityHeadersTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_RootEndpoint_ShouldReturnSecurityHeaders()
    {
        // arrange
        var client = _factory.CreateClient();

        // act
        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        // assert
        response.EnsureSuccessStatusCode();
        response.Headers.GetValues("Content-Security-Policy")
            .ShouldContain(csp => csp.Contains("default-src 'self'"));
        response.Headers.GetValues("X-Content-Type-Options")
            .ShouldContain("nosniff");
        response.Headers.GetValues("X-Frame-Options")
            .ShouldContain("DENY");
        response.Headers.GetValues("Referrer-Policy")
            .ShouldContain("strict-origin-when-cross-origin");
        response.Headers.GetValues("Permissions-Policy")
            .ShouldContain(policy => policy.Contains("microphone=(self)"));
    }

    [Fact]
    public async Task Get_HealthLive_ShouldReturnSecurityHeaders()
    {
        // arrange
        var client = _factory.CreateClient();

        // act
        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        // assert
        response.EnsureSuccessStatusCode();
        response.Headers.ShouldContain(h => h.Key == "Content-Security-Policy");
        response.Headers.ShouldContain(h => h.Key == "X-Content-Type-Options");
    }
}
