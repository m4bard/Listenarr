using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Listenarr.Infrastructure.Models;

#nullable disable

namespace Listenarr.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ListenArrDbContext))]
    [Migration("20260317123000_AddImportBlacklistExtensionsToApplicationSettings")]
    public partial class AddImportBlacklistExtensionsToApplicationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportBlacklistExtensions",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportBlacklistExtensions",
                table: "ApplicationSettings");
        }
    }
}
