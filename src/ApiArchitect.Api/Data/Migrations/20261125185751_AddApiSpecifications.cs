using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiArchitect.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261125185751_AddApiSpecifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ApiSpecifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                OpenApiVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApiSpecifications", x => x.Id);
                table.ForeignKey(
                    name: "FK_ApiSpecifications_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ApiSpecifications_ProjectId_UpdatedAt",
            table: "ApiSpecifications",
            columns: new[] { "ProjectId", "UpdatedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ApiSpecifications");
    }
}
