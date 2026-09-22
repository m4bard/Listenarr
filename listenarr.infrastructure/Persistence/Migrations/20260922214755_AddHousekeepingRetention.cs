using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHousekeepingRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HousekeepingDryRun",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                // True, and this is the whole point of the column. The scaffolder writes a bool
                // column default from the CLR default rather than from the property
                // initializer, so it generated this as false. Left alone, every upgraded
                // database would have come up on its first cycle deleting rows instead of
                // counting them. The schema test asserts this value against the migrated
                // database rather than against the entity, because the entity is exactly what
                // was already right when the scaffolder got it wrong.
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "HousekeepingRetentionDays",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                // The same scaffolder problem in its numeric form: the CLR default for int is
                // zero, and zero is not an unset value here, it is the operator's way of saying
                // "keep everything". An upgraded database would therefore have reported a
                // retention of zero through GET /settings while a fresh one reported thirty,
                // and the two installs would have disagreed about a setting neither operator
                // had touched. Thirty is the declared default and matches Prowlarr's
                // HistoryCleanupDays.
                defaultValue: 30);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HousekeepingDryRun",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "HousekeepingRetentionDays",
                table: "ApplicationSettings");
        }
    }
}
