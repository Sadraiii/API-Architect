using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiArchitect.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261125190252_CollectionsAndEnvironments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Environments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                BaseUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Auth = table.Column<string>(type: "jsonb", nullable: false),
                Headers = table.Column<string>(type: "jsonb", nullable: false),
                Variables = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Environments", x => x.Id);
                table.ForeignKey(
                    name: "FK_Environments_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RequestCollections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RequestCollections", x => x.Id);
                table.ForeignKey(
                    name: "FK_RequestCollections_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RequestFolders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                ParentFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RequestFolders", x => x.Id);
                table.ForeignKey(
                    name: "FK_RequestFolders_RequestCollections_CollectionId",
                    column: x => x.CollectionId,
                    principalTable: "RequestCollections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_RequestFolders_RequestFolders_ParentFolderId",
                    column: x => x.ParentFolderId,
                    principalTable: "RequestFolders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SavedRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Url = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                Body = table.Column<string>(type: "text", nullable: true),
                ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Auth = table.Column<string>(type: "jsonb", nullable: false),
                Headers = table.Column<string>(type: "jsonb", nullable: false),
                QueryParameters = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SavedRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_SavedRequests_RequestCollections_CollectionId",
                    column: x => x.CollectionId,
                    principalTable: "RequestCollections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SavedRequests_RequestFolders_FolderId",
                    column: x => x.FolderId,
                    principalTable: "RequestFolders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Environments_ProjectId",
            table: "Environments",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_RequestCollections_ProjectId",
            table: "RequestCollections",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_RequestFolders_CollectionId",
            table: "RequestFolders",
            column: "CollectionId");

        migrationBuilder.CreateIndex(
            name: "IX_RequestFolders_ParentFolderId",
            table: "RequestFolders",
            column: "ParentFolderId");

        migrationBuilder.CreateIndex(
            name: "IX_SavedRequests_CollectionId",
            table: "SavedRequests",
            column: "CollectionId");

        migrationBuilder.CreateIndex(
            name: "IX_SavedRequests_FolderId",
            table: "SavedRequests",
            column: "FolderId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Environments");

        migrationBuilder.DropTable(
            name: "SavedRequests");

        migrationBuilder.DropTable(
            name: "RequestFolders");

        migrationBuilder.DropTable(
            name: "RequestCollections");
    }
}
