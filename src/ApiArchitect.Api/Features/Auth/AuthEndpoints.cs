using System.Security.Claims;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ApiArchitect.Api.Features.Auth;

/// <summary>
/// Cookie-based sign-in for the single-page app, which is served from the same origin.
/// The cookie is HttpOnly and SameSite=Strict, so scripts can't read it and other sites can't send it.
/// </summary>
public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", Register)
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Create an account and sign in.");

        group.MapPost("/login", Login)
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Sign in with email and password. Sets the auth cookie.");

        group.MapPost("/logout", Logout)
            .RequireAuthorization()
            .WithSummary("Sign out. Clears the auth cookie.");

        group.MapGet("/me", Me)
            .RequireAuthorization()
            .WithSummary("The signed-in user. Returns 401 when not signed in.");
    }

    private static async Task<Results<Ok<CurrentUserResponse>, ValidationProblem>> Register(
        RegisterRequest request, UserManager<AppUser> users, SignInManager<AppUser> signIn, TimeProvider clock)
    {
        var email = request.Email.Trim();
        var user = new AppUser { UserName = email, Email = email, CreatedAt = clock.GetUtcNowForStorage() };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // Group Identity's errors (password rules, duplicate email) by code, in the same
            // ProblemDetails shape that request validation uses.
            var errors = result.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
            return TypedResults.ValidationProblem(errors);
        }

        await signIn.SignInAsync(user, isPersistent: false);
        return TypedResults.Ok(new CurrentUserResponse(email));
    }

    private static async Task<Results<Ok<CurrentUserResponse>, ProblemHttpResult>> Login(
        LoginRequest request, SignInManager<AppUser> signIn)
    {
        var result = await signIn.PasswordSignInAsync(
            request.Email.Trim(), request.Password, request.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return TypedResults.Ok(new CurrentUserResponse(request.Email.Trim()));
        }

        // One message for unknown email, wrong password and lockout, so the response can't be used
        // to find out which emails have accounts.
        return TypedResults.Problem(
            title: "Sign-in failed",
            detail: "The email or password is incorrect, or the account is temporarily locked.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<NoContent> Logout(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static Ok<CurrentUserResponse> Me(ClaimsPrincipal user) =>
        TypedResults.Ok(new CurrentUserResponse(user.FindFirstValue(ClaimTypes.Email) ?? user.Identity!.Name!));
}
