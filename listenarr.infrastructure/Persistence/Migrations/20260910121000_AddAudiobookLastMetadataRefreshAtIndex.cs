using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudiobookLastMetadataRefreshAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Audiobooks_LastMetadataRefreshAt",
                table: "Audiobooks",
                column: "LastMetadataRefreshAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Audiobooks_LastMetadataRefreshAt",
                table: "Audiobooks");
        }
    }
}
