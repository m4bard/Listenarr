using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecentReleaseDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RecentReleaseDiscoveryEnabled",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DiscoveredReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleaseId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    PublishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HandledUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MatchedAudiobookId = table.Column<int>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveredReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveredReleases_Audiobooks_MatchedAudiobookId",
                        column: x => x.MatchedAudiobookId,
                        principalTable: "Audiobooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DiscoveredReleases_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndexerCoverageGaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: false),
                    GapStartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GapEndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DetectedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcknowledgedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexerCoverageGaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndexerCoverageGaps_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndexerDiscoveryCheckpoints",
                columns: table => new
                {
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: false),
                    NewestPublishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NewestReleaseId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LastPollUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSuccessUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FutureDatedItemCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    UndatedItemCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexerDiscoveryCheckpoints", x => x.IndexerId);
                    table.ForeignKey(
                        name: "FK_IndexerDiscoveryCheckpoints_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredReleases_IndexerId_ReleaseId",
                table: "DiscoveredReleases",
                columns: new[] { "IndexerId", "ReleaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredReleases_MatchedAudiobookId",
                table: "DiscoveredReleases",
                column: "MatchedAudiobookId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredReleases_State_FirstSeenUtc",
                table: "DiscoveredReleases",
                columns: new[] { "State", "FirstSeenUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IndexerCoverageGaps_IndexerId_DetectedUtc",
                table: "IndexerCoverageGaps",
                columns: new[] { "IndexerId", "DetectedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoveredReleases");

            migrationBuilder.DropTable(
                name: "IndexerCoverageGaps");

            migrationBuilder.DropTable(
                name: "IndexerDiscoveryCheckpoints");

            migrationBuilder.DropColumn(
                name: "RecentReleaseDiscoveryEnabled",
                table: "ApplicationSettings");
        }
    }
}
