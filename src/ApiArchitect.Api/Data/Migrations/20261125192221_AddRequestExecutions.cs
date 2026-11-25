using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiArchitect.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261125192221_AddRequestExecutions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RequestExecutions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                RequestContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                RequestBody = table.Column<string>(type: "text", nullable: true),
                RequestBodyTruncated = table.Column<bool>(type: "boolean", nullable: false),
                AuthKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                AuthName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                StatusCode = table.Column<int>(type: "integer", nullable: true),
                ReasonPhrase = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ResponseBody = table.Column<string>(type: "text", nullable: false),
                ResponseBodyTruncated = table.Column<bool>(type: "boolean", nullable: false),
                ResponseBodyIsBinary = table.Column<bool>(type: "boolean", nullable: false),
                ResponseSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                DurationMs = table.Column<long>(type: "bigint", nullable: false),
                ErrorCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RequestHeaders = table.Column<string>(type: "jsonb", nullable: false),
                ResponseHeaders = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RequestExecutions", x => x.Id);
                table.ForeignKey(
                    name: "FK_RequestExecutions_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RequestExecutions_ProjectId_ExecutedAt",
            table: "RequestExecutions",
            columns: new[] { "ProjectId", "ExecutedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RequestExecutions");
    }
}
