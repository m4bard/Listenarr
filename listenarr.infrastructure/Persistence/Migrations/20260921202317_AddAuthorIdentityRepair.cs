using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorIdentityRepair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AuthorIdentityCheckedAt",
                table: "MonitoredAuthors",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AuthorIdentityCheckedAt",
                table: "AuthorCacheEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AuthorIdentityRepairDryRun",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                // True, and this is the whole point of the column. An upgraded install must land
                // in the preview state, not in the writing one. The scaffolder writes the CLR
                // default here rather than the property initializer, so leaving it alone would
                // have shipped every existing database straight into repair mode.
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AuthorIdentityRepairEnabled",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AuthorIdentityRepairIntervalHours",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "AuthorIdentityRepairMaxRowsPerRun",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 25);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorIdentityCheckedAt",
                table: "MonitoredAuthors");

            migrationBuilder.DropColumn(
                name: "AuthorIdentityCheckedAt",
                table: "AuthorCacheEntries");

            migrationBuilder.DropColumn(
                name: "AuthorIdentityRepairDryRun",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "AuthorIdentityRepairEnabled",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "AuthorIdentityRepairIntervalHours",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "AuthorIdentityRepairMaxRowsPerRun",
                table: "ApplicationSettings");
        }
    }
}
