using ApiArchitect.Api.Features.Projects;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Project>(project =>
        {
            project.Property(p => p.Name).HasMaxLength(ProjectRules.NameMaxLength);
            project.Property(p => p.Description).HasMaxLength(ProjectRules.DescriptionMaxLength);
            project.Property(p => p.TargetFramework).HasMaxLength(20);

            // Deleting an account removes its projects; nothing else may point at another user's data.
            project.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Serves "my projects, most recently updated first", the dashboard query.
            project.HasIndex(p => new { p.OwnerId, p.UpdatedAt });
        });
    }
}
