using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecycleBinToApplicationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 7, not 0, so an upgraded database gets the same retention as a fresh
            // install. AddColumn's defaultValue backfills existing rows, and 0 means
            // "keep recycled files forever", which is not what the property default says.
            migrationBuilder.AddColumn<int>(
                name: "RecycleBinCleanupDays",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<string>(
                name: "RecycleBinPath",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecycleBinCleanupDays",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "RecycleBinPath",
                table: "ApplicationSettings");
        }
    }
}
