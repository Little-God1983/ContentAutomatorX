using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentAutomatorX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniquePlatformTypePerTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Platforms_TenantId_Type",
                table: "Platforms");

            migrationBuilder.CreateIndex(
                name: "IX_Platforms_TenantId_Type",
                table: "Platforms",
                columns: new[] { "TenantId", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Platforms_TenantId_Type",
                table: "Platforms");

            migrationBuilder.CreateIndex(
                name: "IX_Platforms_TenantId_Type",
                table: "Platforms",
                columns: new[] { "TenantId", "Type" });
        }
    }
}
