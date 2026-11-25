using ApiArchitect.Api.Features.Execution;
using ApiArchitect.Api.Features.Projects;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<RequestExecution> RequestExecutions => Set<RequestExecution>();

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

        builder.Entity<RequestExecution>(execution =>
        {
            execution.Property(e => e.Method).HasMaxLength(10);
            execution.Property(e => e.Url).HasMaxLength(ExecutionRules.UrlMaxLength);
            execution.Property(e => e.RequestContentType).HasMaxLength(ExecutionRules.ContentTypeMaxLength);
            execution.Property(e => e.AuthKind).HasConversion<string>().HasMaxLength(20);
            execution.Property(e => e.AuthName).HasMaxLength(ExecutionRules.HeaderNameMaxLength);
            execution.Property(e => e.ReasonPhrase).HasMaxLength(200);
            execution.Property(e => e.ErrorCode).HasMaxLength(50);
            execution.Property(e => e.ErrorMessage).HasMaxLength(500);
            execution.ComplexCollection(e => e.RequestHeaders, headers => headers.ToJson());
            execution.ComplexCollection(e => e.ResponseHeaders, headers => headers.ToJson());

            execution.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Serves the history list and the prune after each insert.
            execution.HasIndex(e => new { e.ProjectId, e.ExecutedAt });
        });
    }
}
