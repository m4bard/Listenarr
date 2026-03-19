using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Listenarr.Infrastructure.Migrations
{
    [DbContext(typeof(ListenArrDbContext))]
    [Migration("20260318113000_AddMonitoredAuthors")]
    public partial class AddMonitoredAuthors : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonitoredAuthors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AuthorNameNormalized = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AuthorAsin = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Region = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSuccessfulSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoredAuthors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredAuthors_AuthorNameNormalized_Region_Language",
                table: "MonitoredAuthors",
                columns: new[] { "AuthorNameNormalized", "Region", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredAuthors_LastCheckedAt",
                table: "MonitoredAuthors",
                column: "LastCheckedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonitoredAuthors");
        }
    }
}
