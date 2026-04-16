using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Seren.Application.Abstractions;
using Shouldly;
using Xunit;

namespace Seren.Server.Api.IntegrationTests;

/// <summary>
/// Integration tests for the JWT-protected WebSocket endpoint. Boots a
/// WebApplicationFactory instance with <c>Auth:RequireAuthentication=true</c>
/// and a fixed signing secret, then asserts accept/reject behaviour for
/// each token state the JwtBearer events must handle.
/// </summary>
public sealed class WebSocketAuthTests : IClassFixture<WebSocketAuthTests.AuthEnabledFactory>
{
    private const string JwtSecret = "integration-test-secret-please-never-ship-to-production";
    private const string Issuer = "seren.hub.tests";
    private const string Audience = "seren.clients.tests";

    private readonly AuthEnabledFactory _factory;

    public WebSocketAuthTests(AuthEnabledFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Handshake_WithValidToken_ShouldAccept()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        var token = GenerateToken(expiresInMinutes: 15);
        var client = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, $"ws?access_token={token}");

        // act
        using var socket = await client.ConnectAsync(uri, ct);

        // assert
        socket.State.ShouldBe(WebSocketState.Open);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    [Fact]
    public async Task Handshake_WithExpiredToken_ShouldBeRejected()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        var token = GenerateToken(expiresInMinutes: -5);
        var client = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, $"ws?access_token={token}");

        // act + assert
        await Should.ThrowAsync<Exception>(async () => await client.ConnectAsync(uri, ct));
    }

    [Fact]
    public async Task Handshake_WithWrongAudience_ShouldBeRejected()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        var token = GenerateToken(expiresInMinutes: 15, audience: "some.other.audience");
        var client = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, $"ws?access_token={token}");

        // act + assert
        await Should.ThrowAsync<Exception>(async () => await client.ConnectAsync(uri, ct));
    }

    [Fact]
    public async Task Handshake_WithoutToken_ShouldBeRejected()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, "ws");

        // act + assert
        await Should.ThrowAsync<Exception>(async () => await client.ConnectAsync(uri, ct));
    }

    [Fact]
    public async Task Handshake_WithRevokedToken_ShouldBeRejected()
    {
        // arrange
        var ct = TestContext.Current.CancellationToken;
        var (token, jti) = GenerateTokenWithJti(expiresInMinutes: 15);

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITokenRevocationStore>();
        await store.RevokeAsync(jti, DateTimeOffset.UtcNow.AddMinutes(30), ct);

        var client = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, $"ws?access_token={token}");

        // act + assert
        await Should.ThrowAsync<Exception>(async () => await client.ConnectAsync(uri, ct));
    }

    private static string GenerateToken(int expiresInMinutes, string? audience = null)
    {
        var (token, _) = GenerateTokenWithJti(expiresInMinutes, audience);
        return token;
    }

    private static (string Token, string Jti) GenerateTokenWithJti(int expiresInMinutes, string? audience = null)
    {
        var jti = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var notBefore = now.AddMinutes(Math.Min(expiresInMinutes, 0) - 1);
        var expires = now.AddMinutes(expiresInMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(ClaimTypes.Role, "user"),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), jti);
    }

    /// <summary>
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> that patches the Auth
    /// configuration before the host builds, forcing JWT requirement on.
    /// </summary>
    public sealed class AuthEnabledFactory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:RequireAuthentication"] = "true",
                    ["Auth:JwtSecret"] = JwtSecret,
                    ["Auth:Issuer"] = Issuer,
                    ["Auth:Audience"] = Audience,
                    ["Auth:TokenExpirationMinutes"] = "15",
                });
            });

            return base.CreateHost(builder);
        }
    }
}
