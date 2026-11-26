using Microsoft.AspNetCore.Identity;

namespace ApiArchitect.Api.Data;

/// <summary>
/// The signed-in account. Identity owns the email, password hash and lockout state;
/// we only add what Identity does not track.
/// </summary>
public sealed class AppUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the user finished or skipped the introduction tour. Null means show it on next visit.</summary>
    public DateTimeOffset? IntroSeenAt { get; set; }
}
