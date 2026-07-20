using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentAutomatorX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProposalBaselineTitleAndUniqueRevisionOrdinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IssueRevisions_PostId_Stack_Ordinal",
                table: "IssueRevisions");

            migrationBuilder.AddColumn<string>(
                name: "BaselineTitle",
                table: "IssueSectionProposals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueRevisions_PostId_Stack_Ordinal",
                table: "IssueRevisions",
                columns: new[] { "PostId", "Stack", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IssueRevisions_PostId_Stack_Ordinal",
                table: "IssueRevisions");

            migrationBuilder.DropColumn(
                name: "BaselineTitle",
                table: "IssueSectionProposals");

            migrationBuilder.CreateIndex(
                name: "IX_IssueRevisions_PostId_Stack_Ordinal",
                table: "IssueRevisions",
                columns: new[] { "PostId", "Stack", "Ordinal" });
        }
    }
}
