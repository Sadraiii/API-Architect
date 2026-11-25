using Microsoft.AspNetCore.Identity;

namespace ApiArchitect.Api.Data;

/// <summary>
/// The signed-in account. Identity owns the email, password hash and lockout state;
/// we only add what Identity does not track.
/// </summary>
public sealed class AppUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; init; }
}
