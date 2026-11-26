using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiArchitect.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261126044909_AddIntroSeenAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "IntroSeenAt",
            table: "AspNetUsers",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IntroSeenAt",
            table: "AspNetUsers");
    }
}
