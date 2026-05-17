using Listenarr.Domain.Models.Enumerations;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Test : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TempCompletedFileAction",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: FileAction.Copy);

            migrationBuilder.Sql("UPDATE ApplicationSettings SET TempCompletedFileAction = 0 WHERE lower(trim(CompletedFileAction)) IN ('none', '0')");
            migrationBuilder.Sql("UPDATE ApplicationSettings SET TempCompletedFileAction = 1 WHERE lower(trim(CompletedFileAction)) IN ('move', '1')");
            migrationBuilder.Sql("UPDATE ApplicationSettings SET TempCompletedFileAction = 2 WHERE lower(trim(CompletedFileAction)) IN ('copy', '2')");
            migrationBuilder.Sql("UPDATE ApplicationSettings SET TempCompletedFileAction = 3 WHERE lower(trim(CompletedFileAction)) IN ('hardlink/copy', 'hardlinkcopy', '3')");

            migrationBuilder.DropColumn(name: "CompletedFileAction", table: "ApplicationSettings");
            migrationBuilder.RenameColumn(name: "TempCompletedFileAction", table: "ApplicationSettings", newName: "CompletedFileAction");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TempCompletedFileAction",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "Copy");

            migrationBuilder.Sql("UPDATE ApplicationSettings SET TempCompletedFileAction = 'None'          WHERE CompletedFileAction = 0");
            migrationBuilder.Sql("UPDATE ApplicationSettings SET TempCompletedFileAction = 'Move'          WHERE CompletedFileAction = 1");
            migrationBuilder.Sql("UPDATE ApplicationSettings SET TempCompletedFileAction = 'Copy'          WHERE CompletedFileAction = 2");
            migrationBuilder.Sql("UPDATE ApplicationSettings SET TempCompletedFileAction = 'Hardlink/Copy' WHERE CompletedFileAction = 3");

            migrationBuilder.DropColumn(name: "CompletedFileAction", table: "ApplicationSettings");
            migrationBuilder.RenameColumn(
                name: "TempCompletedFileAction",
                table: "ApplicationSettings",
                newName: "CompletedFileAction");
        }
    }
}
