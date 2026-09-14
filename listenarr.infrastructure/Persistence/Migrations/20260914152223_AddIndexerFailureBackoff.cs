using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexerFailureBackoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DisabledTill",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EscalationLevel",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "InitialFailure",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastFailureReason",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MostRecentFailure",
                table: "Indexers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisabledTill",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "EscalationLevel",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "InitialFailure",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "LastFailureReason",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "MostRecentFailure",
                table: "Indexers");
        }
    }
}
