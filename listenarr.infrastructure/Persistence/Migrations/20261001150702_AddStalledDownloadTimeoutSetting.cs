using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStalledDownloadTimeoutSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StalledDownloadTimeoutHours",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                // Must equal the model default. SQLite gives existing rows the column default, so
                // this is what an upgraded install gets, and 0 keeps stall handling off there just
                // as on a fresh install. StalledDownloadTimeoutMigrationTests pins it.
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StalledDownloadTimeoutHours",
                table: "ApplicationSettings");
        }
    }
}
