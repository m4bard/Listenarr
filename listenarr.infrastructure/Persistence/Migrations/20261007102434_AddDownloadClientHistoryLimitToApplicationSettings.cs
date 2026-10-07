using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadClientHistoryLimitToApplicationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 60, not the 0 the scaffolder emits. The scaffolder uses the CLR default for the
            // type and does not see ApplicationSettings's property initialiser, so an upgraded
            // database would back-fill 0 while a fresh install got 60. Zero means Take(0) in
            // NzbgetHistoryReader.ReadAsync -- an upgraded instance would see no NZBGet history
            // at all, a worse regression than the unbounded fetch this migration exists to
            // bound. Same shape as BackupRetentionDays's own fix,
            // 20260922212752_AddBackupRetentionDaysToApplicationSettings.cs.
            migrationBuilder.AddColumn<int>(
                name: "DownloadClientHistoryLimit",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 60);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DownloadClientHistoryLimit",
                table: "ApplicationSettings");
        }
    }
}
