using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProwlarrImportSettingsToApplicationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProwlarrApiKeyEncrypted",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProwlarrPort",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProwlarrUrl",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProwlarrApiKeyEncrypted",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "ProwlarrPort",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "ProwlarrUrl",
                table: "ApplicationSettings");
        }
    }
}
