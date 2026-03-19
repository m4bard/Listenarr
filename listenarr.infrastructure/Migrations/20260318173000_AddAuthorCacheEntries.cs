using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Listenarr.Infrastructure.Migrations
{
    [DbContext(typeof(ListenArrDbContext))]
    [Migration("20260318173000_AddAuthorCacheEntries")]
    public partial class AddAuthorCacheEntries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthorCacheEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AuthorNameNormalized = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AuthorAsin = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Region = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SimilarAuthors = table.Column<string>(type: "TEXT", nullable: true),
                    CatalogBooks = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorCacheEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorCacheEntries_AuthorAsin_Region",
                table: "AuthorCacheEntries",
                columns: new[] { "AuthorAsin", "Region" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorCacheEntries_AuthorNameNormalized_Region",
                table: "AuthorCacheEntries",
                columns: new[] { "AuthorNameNormalized", "Region" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthorCacheEntries");
        }
    }
}
