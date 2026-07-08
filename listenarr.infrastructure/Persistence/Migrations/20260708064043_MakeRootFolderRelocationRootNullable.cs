using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeRootFolderRelocationRootNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RootFolderRelocations_RootFolders_RootFolderId",
                table: "RootFolderRelocations");

            migrationBuilder.AlterColumn<int>(
                name: "RootFolderId",
                table: "RootFolderRelocations",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

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

            migrationBuilder.AlterColumn<int>(
                name: "RootFolderId",
                table: "RootFolderRelocations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

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
