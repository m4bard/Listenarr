using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoredSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonitoredSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SeriesNameNormalized = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SeriesAsin = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Region = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSuccessfulSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoredSeries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredSeries_LastCheckedAt",
                table: "MonitoredSeries",
                column: "LastCheckedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredSeries_SeriesNameNormalized_Region_Language",
                table: "MonitoredSeries",
                columns: new[] { "SeriesNameNormalized", "Region", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonitoredSeries");
        }
    }
}
