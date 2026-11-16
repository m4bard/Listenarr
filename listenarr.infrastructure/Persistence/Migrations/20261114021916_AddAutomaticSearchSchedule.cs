using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticSearchSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AutomaticSearchDueUtc",
                table: "Audiobooks",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "AutomaticSearchFailureCount",
                table: "Audiobooks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AutomaticSearchMaxBooksPerCycle",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 25);

            migrationBuilder.AddColumn<int>(
                name: "AutomaticSearchMaxRequestsPerCycle",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 300);

            migrationBuilder.CreateTable(
                name: "AudiobookIndexerSearchStates",
                columns: table => new
                {
                    AudiobookId = table.Column<int>(type: "INTEGER", nullable: false),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NoResultCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    LastOutcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastCompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudiobookIndexerSearchStates", x => new { x.AudiobookId, x.IndexerId });
                    table.ForeignKey(
                        name: "FK_AudiobookIndexerSearchStates_Audiobooks_AudiobookId",
                        column: x => x.AudiobookId,
                        principalTable: "Audiobooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AudiobookIndexerSearchStates_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Audiobooks_AutomaticSearchDue",
                table: "Audiobooks",
                column: "AutomaticSearchDueUtc",
                filter: "\"Monitored\" AND \"QualityProfileId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AudiobookIndexerSearchStates_IndexerId",
                table: "AudiobookIndexerSearchStates",
                column: "IndexerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudiobookIndexerSearchStates");

            migrationBuilder.DropIndex(
                name: "IX_Audiobooks_AutomaticSearchDue",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AutomaticSearchDueUtc",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AutomaticSearchFailureCount",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AutomaticSearchMaxBooksPerCycle",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "AutomaticSearchMaxRequestsPerCycle",
                table: "ApplicationSettings");
        }
    }
}
