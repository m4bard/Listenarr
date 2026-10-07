using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStalledDownloadFloorSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StalledDownloadFloorPercent",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                // Must equal the model default (1m), not the scaffolder's CLR default (0m).
                // SQLite backfills an existing row from the column default, and 0 means the
                // floor is OFF -- an upgraded install would silently lose the floor that a
                // fresh install gets, the same trap StalledDownloadTimeoutHours's own migration
                // and #357's DownloadClientHistoryLimit migration both document.
                defaultValue: 1m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StalledDownloadFloorPercent",
                table: "ApplicationSettings");
        }
    }
}
