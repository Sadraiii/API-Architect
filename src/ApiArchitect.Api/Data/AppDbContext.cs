using ApiArchitect.Api.Features.Projects;
using ApiArchitect.Api.Features.Specifications;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ApiSpecification> ApiSpecifications => Set<ApiSpecification>();

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

        builder.Entity<ApiSpecification>(specification =>
        {
            specification.Property(s => s.Name).HasMaxLength(SpecificationRules.NameMaxLength);
            specification.Property(s => s.Title).HasMaxLength(SpecificationRules.TitleMaxLength);
            specification.Property(s => s.Version).HasMaxLength(SpecificationRules.VersionMaxLength);
            specification.Property(s => s.OpenApiVersion).HasMaxLength(SpecificationRules.OpenApiVersionMaxLength);

            specification.HasOne(s => s.Project)
                .WithMany()
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Serves "a project's specifications, most recently updated first".
            specification.HasIndex(s => new { s.ProjectId, s.UpdatedAt });
        });
    }
}
