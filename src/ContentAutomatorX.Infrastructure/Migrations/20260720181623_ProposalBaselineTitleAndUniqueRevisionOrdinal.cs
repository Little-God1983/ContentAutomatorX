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

            // The unique index below would abort the whole migration on any database that already
            // holds a tied ordinal — the exact corruption this index exists to prevent, which could
            // have been written before it existed. Collapse each tie to one row first, keeping the
            // most recent, so an affected database upgrades instead of refusing to start.
            migrationBuilder.Sql("""
                DELETE FROM "IssueRevisions" WHERE "Id" NOT IN (
                    SELECT "Id" FROM "IssueRevisions" GROUP BY "PostId", "Stack", "Ordinal"
                    HAVING "CreatedAt" = MAX("CreatedAt")
                );
                """);

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
