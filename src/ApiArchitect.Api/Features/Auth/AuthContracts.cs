using System.ComponentModel.DataAnnotations;

namespace ApiArchitect.Api.Features.Auth;

public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128)] string Password);

public sealed record LoginRequest(
    [Required, StringLength(256)] string Email,
    [Required, StringLength(128)] string Password,
    bool RememberMe = false);

public sealed record CurrentUserResponse(string Email, bool IntroSeen);
