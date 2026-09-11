using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataRefreshSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MetadataRefreshEnabled",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MetadataRefreshIntervalHours",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "MetadataRefreshMinimumSpacingMs",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<int>(
                name: "MetadataRefreshRequestsPerHour",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "MetadataRefreshStaleAfterDays",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetadataRefreshEnabled",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "MetadataRefreshIntervalHours",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "MetadataRefreshMinimumSpacingMs",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "MetadataRefreshRequestsPerHour",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "MetadataRefreshStaleAfterDays",
                table: "ApplicationSettings");
        }
    }
}
