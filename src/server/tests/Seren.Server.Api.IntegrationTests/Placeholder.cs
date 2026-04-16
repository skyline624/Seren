using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace Seren.Server.Api.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Root_ShouldReturnBanner()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        // act
        var response = await client.GetAsync(new Uri("/", UriKind.Relative), ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        // assert
        response.IsSuccessStatusCode.ShouldBeTrue();
        content.ShouldContain("Seren Hub");
    }

    [Fact]
    public async Task Liveness_ShouldReturn200()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), ct);

        response.IsSuccessStatusCode.ShouldBeTrue();
        (await response.Content.ReadAsStringAsync(ct)).ShouldBe("alive");
    }

    [Fact]
    public async Task Readiness_ShouldReturn200_WhenHealthy()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), ct);

        response.IsSuccessStatusCode.ShouldBeTrue();
    }
}
