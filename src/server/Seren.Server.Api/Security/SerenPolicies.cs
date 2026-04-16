namespace Seren.Server.Api.Security;

/// <summary>
/// Well-known authorization policy names registered in the DI container.
/// </summary>
public static class SerenPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string RequireAuth = "RequireAuth";
}
