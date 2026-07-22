using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentAutomatorX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PerJobLlmSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantLlmSettings_TenantId",
                table: "TenantLlmSettings");

            migrationBuilder.AddColumn<string>(
                name: "Job",
                table: "TenantLlmSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantLlmSettings_TenantId",
                table: "TenantLlmSettings",
                column: "TenantId",
                unique: true,
                filter: "\"Job\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLlmSettings_TenantId_Job",
                table: "TenantLlmSettings",
                columns: new[] { "TenantId", "Job" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantLlmSettings_TenantId",
                table: "TenantLlmSettings");

            migrationBuilder.DropIndex(
                name: "IX_TenantLlmSettings_TenantId_Job",
                table: "TenantLlmSettings");

            // Per-job overrides cannot survive without the Job column. Drop them first so collapsing
            // back to a unique index on TenantId alone does not fail on tenants that have both a
            // default row and one or more job rows. Only the tenant-default rows (Job IS NULL) remain.
            migrationBuilder.Sql("DELETE FROM \"TenantLlmSettings\" WHERE \"Job\" IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "Job",
                table: "TenantLlmSettings");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLlmSettings_TenantId",
                table: "TenantLlmSettings",
                column: "TenantId",
                unique: true);
        }
    }
}
