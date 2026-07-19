using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentAutomatorX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContentItemNormalizedUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedUrl",
                table: "ContentItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_TenantId_NormalizedUrl",
                table: "ContentItems",
                columns: new[] { "TenantId", "NormalizedUrl" },
                unique: true,
                filter: "\"NormalizedUrl\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContentItems_TenantId_NormalizedUrl",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "NormalizedUrl",
                table: "ContentItems");
        }
    }
}
