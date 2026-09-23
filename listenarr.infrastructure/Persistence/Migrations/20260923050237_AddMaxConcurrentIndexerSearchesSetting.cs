using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxConcurrentIndexerSearchesSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxConcurrentIndexerSearches",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                // Scaffolded as 0 (the CLR default) and corrected by hand: 4 is the ceiling that
                // was hardcoded before this was a setting, and SQLite gives existing rows the
                // column default. IndexerSearchConcurrencyMigrationTests pins it.
                defaultValue: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxConcurrentIndexerSearches",
                table: "ApplicationSettings");
        }
    }
}
