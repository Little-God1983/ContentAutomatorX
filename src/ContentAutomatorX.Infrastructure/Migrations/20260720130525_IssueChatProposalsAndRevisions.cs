using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentAutomatorX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IssueChatProposalsAndRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IssueChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueChatMessages_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Stack = table.Column<string>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueRevisions_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueSectionProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposedTitle = table.Column<string>(type: "TEXT", nullable: true),
                    ProposedBodyMd = table.Column<string>(type: "TEXT", nullable: true),
                    BaselineBodyMd = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueSectionProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueSectionProposals_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueChatMessages_PostId",
                table: "IssueChatMessages",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueRevisions_PostId_Stack_Ordinal",
                table: "IssueRevisions",
                columns: new[] { "PostId", "Stack", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueSectionProposals_PostId",
                table: "IssueSectionProposals",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueSectionProposals_SectionId",
                table: "IssueSectionProposals",
                column: "SectionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueChatMessages");

            migrationBuilder.DropTable(
                name: "IssueRevisions");

            migrationBuilder.DropTable(
                name: "IssueSectionProposals");
        }
    }
}
