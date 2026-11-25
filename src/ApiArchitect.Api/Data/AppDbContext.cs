using ApiArchitect.Api.Features.Collections;
using ApiArchitect.Api.Features.Environments;
using ApiArchitect.Api.Features.Projects;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<RequestCollection> Collections => Set<RequestCollection>();

    public DbSet<RequestFolder> Folders => Set<RequestFolder>();

    public DbSet<SavedRequest> SavedRequests => Set<SavedRequest>();

    public DbSet<ApiEnvironment> Environments => Set<ApiEnvironment>();

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

        builder.Entity<RequestCollection>(collection =>
        {
            collection.ToTable("RequestCollections");
            collection.Property(c => c.Name).HasMaxLength(CollectionRules.NameMaxLength);
            collection.Property(c => c.Description).HasMaxLength(CollectionRules.DescriptionMaxLength);
            collection.HasOne(c => c.Project).WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
            collection.HasIndex(c => c.ProjectId);
        });

        builder.Entity<RequestFolder>(folder =>
        {
            folder.ToTable("RequestFolders");
            folder.Property(f => f.Name).HasMaxLength(CollectionRules.NameMaxLength);
            folder.HasOne(f => f.Collection).WithMany().HasForeignKey(f => f.CollectionId).OnDelete(DeleteBehavior.Cascade);

            // Deleting a folder deletes its subfolders, and through SavedRequest.FolderId, their requests.
            folder.HasOne<RequestFolder>().WithMany().HasForeignKey(f => f.ParentFolderId).OnDelete(DeleteBehavior.Cascade);
            folder.HasIndex(f => f.CollectionId);
        });

        builder.Entity<SavedRequest>(request =>
        {
            request.ToTable("SavedRequests");
            request.Property(r => r.Name).HasMaxLength(CollectionRules.NameMaxLength);
            request.Property(r => r.Method).HasMaxLength(10);
            request.Property(r => r.Url).HasMaxLength(CollectionRules.UrlMaxLength);
            request.Property(r => r.ContentType).HasMaxLength(CollectionRules.ContentTypeMaxLength);
            request.HasOne(r => r.Collection).WithMany().HasForeignKey(r => r.CollectionId).OnDelete(DeleteBehavior.Cascade);
            request.HasOne<RequestFolder>().WithMany().HasForeignKey(r => r.FolderId).OnDelete(DeleteBehavior.Cascade);
            request.HasIndex(r => r.CollectionId);
            request.HasIndex(r => r.FolderId);

            // Headers, parameters and auth are always read and written with the request, so they live in
            // jsonb columns on its row rather than in tables of their own.
            request.ComplexCollection(r => r.Headers, b => b.ToJson());
            request.ComplexCollection(r => r.QueryParameters, b => b.ToJson());
            request.ComplexProperty(r => r.Auth, b => b.ToJson());
        });

        builder.Entity<ApiEnvironment>(environment =>
        {
            environment.ToTable("Environments");
            environment.Property(e => e.Name).HasMaxLength(EnvironmentRules.NameMaxLength);
            environment.Property(e => e.BaseUrl).HasMaxLength(EnvironmentRules.BaseUrlMaxLength);
            environment.HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Cascade);
            environment.HasIndex(e => e.ProjectId);
            environment.ComplexCollection(e => e.Variables, b => b.ToJson());
            environment.ComplexCollection(e => e.Headers, b => b.ToJson());
            environment.ComplexProperty(e => e.Auth, b => b.ToJson());
        });
    }
}
