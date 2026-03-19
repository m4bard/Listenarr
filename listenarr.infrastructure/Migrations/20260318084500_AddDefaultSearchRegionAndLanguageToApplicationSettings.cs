using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Listenarr.Infrastructure.Models;

#nullable disable

namespace Listenarr.Infrastructure.Migrations
{
    [DbContext(typeof(ListenArrDbContext))]
    [Migration("20260318084500_AddDefaultSearchRegionAndLanguageToApplicationSettings")]
    public partial class AddDefaultSearchRegionAndLanguageToApplicationSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultSearchLanguage",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "english");

            migrationBuilder.AddColumn<string>(
                name: "DefaultSearchRegion",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "us");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultSearchLanguage",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "DefaultSearchRegion",
                table: "ApplicationSettings");
        }
    }
}
