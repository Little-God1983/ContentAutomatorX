using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentAutomatorX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NewsletterTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NewsletterTemplateId",
                table: "Recipes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "IssueSections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaselineCategory",
                table: "IssueSectionProposals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposedCategory",
                table: "IssueSectionProposals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NewsletterTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Html = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsletterTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsletterTemplates_TenantId",
                table: "NewsletterTemplates",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewsletterTemplates");

            migrationBuilder.DropColumn(
                name: "NewsletterTemplateId",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "IssueSections");

            migrationBuilder.DropColumn(
                name: "BaselineCategory",
                table: "IssueSectionProposals");

            migrationBuilder.DropColumn(
                name: "ProposedCategory",
                table: "IssueSectionProposals");
        }
    }
}
