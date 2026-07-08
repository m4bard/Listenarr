using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SetRootFolderRelocationRootDeleteBehavior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RootFolderRelocations_RootFolders_RootFolderId",
                table: "RootFolderRelocations");

            migrationBuilder.AddForeignKey(
                name: "FK_RootFolderRelocations_RootFolders_RootFolderId",
                table: "RootFolderRelocations",
                column: "RootFolderId",
                principalTable: "RootFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RootFolderRelocations_RootFolders_RootFolderId",
                table: "RootFolderRelocations");

            migrationBuilder.AddForeignKey(
                name: "FK_RootFolderRelocations_RootFolders_RootFolderId",
                table: "RootFolderRelocations",
                column: "RootFolderId",
                principalTable: "RootFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
