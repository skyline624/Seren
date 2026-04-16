using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Seren.Application.Abstractions;
using Seren.Server.Api.Security;

namespace Seren.Server.Api.Endpoints;

/// <summary>
/// Auth-related HTTP endpoints: logout (token revocation) and whoami.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/auth").WithTags("auth");

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization(SerenPolicies.RequireAuth)
            .WithName("Logout");

        group.MapGet("/whoami", WhoAmI)
            .RequireAuthorization(SerenPolicies.RequireAuth)
            .WithName("WhoAmI");

        return routes;
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, ITokenService tokenService, CancellationToken cancellationToken)
    {
        var rawToken = ExtractBearerToken(context);
        if (rawToken is null)
        {
            return Results.Unauthorized();
        }

        var revoked = await tokenService.RevokeAsync(rawToken, cancellationToken).ConfigureAwait(false);
        return revoked
            ? Results.Ok(new { revoked = true })
            : Results.BadRequest(new { error = "Token could not be parsed" });
    }

    private static IResult WhoAmI(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? string.Empty;
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return Results.Ok(new { sub, role });
    }

    private static string? ExtractBearerToken(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("Authorization", out var header))
        {
            var value = header.ToString();
            const string prefix = "Bearer ";
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return value[prefix.Length..].Trim();
            }
        }

        if (context.Request.Query.TryGetValue("access_token", out var query))
        {
            return query.ToString();
        }

        return null;
    }
}
