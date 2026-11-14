using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexerRequestBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyRequestLimit",
                table: "Indexers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IndexerInteractiveReservePercent",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 25);

            migrationBuilder.CreateTable(
                name: "IndexerRequestUsages",
                columns: table => new
                {
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: false),
                    HourStartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexerRequestUsages", x => new { x.IndexerId, x.HourStartUtc });
                    table.ForeignKey(
                        name: "FK_IndexerRequestUsages_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndexerRequestUsages_HourStartUtc",
                table: "IndexerRequestUsages",
                column: "HourStartUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndexerRequestUsages");

            migrationBuilder.DropColumn(
                name: "DailyRequestLimit",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "IndexerInteractiveReservePercent",
                table: "ApplicationSettings");
        }
    }
}
