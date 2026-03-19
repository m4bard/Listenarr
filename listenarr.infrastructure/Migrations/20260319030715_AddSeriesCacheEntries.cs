using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesCacheEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeriesCacheEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SeriesNameNormalized = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SeriesAsin = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Region = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CatalogBooks = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesCacheEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeriesCacheEntries_SeriesAsin_Region",
                table: "SeriesCacheEntries",
                columns: new[] { "SeriesAsin", "Region" });

            migrationBuilder.CreateIndex(
                name: "IX_SeriesCacheEntries_SeriesNameNormalized_Region",
                table: "SeriesCacheEntries",
                columns: new[] { "SeriesNameNormalized", "Region" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeriesCacheEntries");
        }
    }
}
