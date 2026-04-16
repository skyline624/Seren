using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Infrastructure.Authentication;
using Seren.Infrastructure.Realtime;

namespace Seren.Server.Api.Endpoints;

/// <summary>
/// Maps the Seren WebSocket endpoint configured via <see cref="SerenHubOptions"/>.
/// When authentication is required, validates the JWT from the query string
/// or Authorization header before accepting the connection.
/// </summary>
public static class WebSocketEndpoint
{
    /// <summary>
    /// Maps a GET endpoint at <c>Seren:WebSocket:Path</c> that upgrades the
    /// incoming HTTP request to a WebSocket and delegates its lifetime to
    /// <see cref="SerenWebSocketSessionProcessor"/>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="requireAuthentication">
    /// When <c>true</c>, the JWT must be valid before the WebSocket upgrade is accepted.
    /// When <c>false</c>, authentication is optional (dev mode).
    /// </param>
    public static IEndpointRouteBuilder MapSerenWebSocketEndpoint(
        this IEndpointRouteBuilder endpoints,
        bool requireAuthentication)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<SerenHubOptions>>().Value;

        endpoints.MapGet(options.Path, HandleAsync);

        return endpoints;
    }

    private static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket upgrade required.").ConfigureAwait(false);
            return;
        }

        var authOptions = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;

        if (authOptions.RequireAuthentication)
        {
            var tokenService = context.RequestServices.GetRequiredService<ITokenService>();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Seren.Server.Api.Endpoints.WebSocketEndpoint");

            var token = ExtractToken(context);

            if (string.IsNullOrEmpty(token))
            {
                logger.LogWarning("WebSocket connection rejected: missing authentication token.");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Authentication required.").ConfigureAwait(false);
                return;
            }

            var principal = tokenService.ValidateToken(token);
            if (principal is null)
            {
                logger.LogWarning("WebSocket connection rejected: invalid or expired token.");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Invalid or expired token.").ConfigureAwait(false);
                return;
            }

            // Reject tokens that have been explicitly revoked (logout).
            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (!string.IsNullOrWhiteSpace(jti))
            {
                var revocationStore = context.RequestServices.GetRequiredService<ITokenRevocationStore>();
                var isRevoked = await revocationStore
                    .IsRevokedAsync(jti, context.RequestAborted)
                    .ConfigureAwait(false);
                if (isRevoked)
                {
                    logger.LogWarning("WebSocket connection rejected: token has been revoked (jti={Jti}).", jti);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Token has been revoked.").ConfigureAwait(false);
                    return;
                }
            }

            // Attach the principal so downstream code can access claims.
            context.User = principal;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

        var processor = context.RequestServices.GetRequiredService<SerenWebSocketSessionProcessor>();
        await processor.ProcessAsync(socket, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts a JWT token from the <c>Authorization: Bearer</c> header or the
    /// <c>access_token</c> query parameter (used by WebSocket clients that cannot
    /// set headers during the upgrade handshake).
    /// </summary>
    private static string? ExtractToken(HttpContext context)
    {
        // 1. Authorization header: "Bearer <token>"
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        // 2. Query string: ?access_token=<token>
        if (context.Request.Query.TryGetValue("access_token", out var queryToken))
        {
            return queryToken.ToString();
        }

        return null;
    }
}
