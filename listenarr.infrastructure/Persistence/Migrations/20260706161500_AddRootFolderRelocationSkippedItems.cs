using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ListenArrDbContext))]
    [Migration("20260706161500_AddRootFolderRelocationSkippedItems")]
    public partial class AddRootFolderRelocationSkippedItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceCaseSensitivityMode",
                table: "RootFolderRelocations",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Auto");

            migrationBuilder.CreateTable(
                name: "RootFolderRelocationSkippedItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AudiobookId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RootFolderRelocationSkippedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RootFolderRelocationSkippedItems_RootFolderRelocations_RelocationId",
                        column: x => x.RelocationId,
                        principalTable: "RootFolderRelocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RootFolderRelocationSkippedItems_RelocationId_AudiobookId",
                table: "RootFolderRelocationSkippedItems",
                columns: new[] { "RelocationId", "AudiobookId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RootFolderRelocationSkippedItems");

            migrationBuilder.DropColumn(
                name: "SourceCaseSensitivityMode",
                table: "RootFolderRelocations");
        }
    }
}
