using System.Security.Claims;

namespace ApiArchitect.Api.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The Identity user id of an authenticated caller. Only call this from endpoints that
    /// require authorization; a missing id there means the auth setup is broken, so it throws.
    /// </summary>
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated user has no NameIdentifier claim.");
}
