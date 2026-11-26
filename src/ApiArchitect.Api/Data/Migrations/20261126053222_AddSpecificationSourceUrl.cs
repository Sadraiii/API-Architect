using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiArchitect.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261126053222_AddSpecificationSourceUrl : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SourceUrl",
            table: "ApiSpecifications",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SourceUrl",
            table: "ApiSpecifications");
    }
}
