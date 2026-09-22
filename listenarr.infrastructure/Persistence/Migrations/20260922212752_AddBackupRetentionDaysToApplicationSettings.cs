using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupRetentionDaysToApplicationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 28, not the 0 the scaffolder emits. The scaffolder uses the CLR default for the
            // type, and it does not see the property initialiser on ApplicationSettings, so an
            // upgraded database would back-fill 0 while a fresh install got 28. Zero means "never
            // sweep" to BackupService, so the two would quietly behave differently forever. The
            // Version column at 20260621002226_AddApplicationSettingsConcurrency.cs:17 carries a
            // hand-set default for the same reason.
            migrationBuilder.AddColumn<int>(
                name: "BackupRetentionDays",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 28);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackupRetentionDays",
                table: "ApplicationSettings");
        }
    }
}
