using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentAutomatorX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NewsletterImageKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageKey",
                table: "IssueSections",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageKey",
                table: "IssueSections");
        }
    }
}
