using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentAutomatorX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecipeTargetPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TargetPlatformId",
                table: "Recipes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetPlatformId",
                table: "Recipes");
        }
    }
}
