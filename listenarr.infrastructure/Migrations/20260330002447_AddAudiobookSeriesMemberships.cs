using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAudiobookSeriesMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AudiobookSeriesMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AudiobookId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SeriesNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SeriesAsin = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudiobookSeriesMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudiobookSeriesMemberships_Audiobooks_AudiobookId",
                        column: x => x.AudiobookId,
                        principalTable: "Audiobooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudiobookSeriesMemberships_AudiobookId",
                table: "AudiobookSeriesMemberships",
                column: "AudiobookId");

            migrationBuilder.CreateIndex(
                name: "IX_AudiobookSeriesMemberships_AudiobookId_IsPrimary",
                table: "AudiobookSeriesMemberships",
                columns: new[] { "AudiobookId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_AudiobookSeriesMemberships_AudiobookId_SortOrder",
                table: "AudiobookSeriesMemberships",
                columns: new[] { "AudiobookId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudiobookSeriesMemberships");
        }
    }
}
