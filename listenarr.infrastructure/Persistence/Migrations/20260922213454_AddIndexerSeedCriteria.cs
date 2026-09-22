using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexerSeedCriteria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SeedRatio",
                table: "Indexers",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeedTime",
                table: "Indexers",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeedRatio",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "SeedTime",
                table: "Indexers");
        }
    }
}
