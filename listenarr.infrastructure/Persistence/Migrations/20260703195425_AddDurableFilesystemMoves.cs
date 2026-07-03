using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableFilesystemMoves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaseSensitivityMode",
                table: "RootFolders",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Auto");

            migrationBuilder.AddColumn<string>(
                name: "PathIdentityKey",
                table: "RootFolders",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PathIdentityState",
                table: "RootFolders",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unavailable");

            migrationBuilder.AddColumn<string>(
                name: "ResolvedCaseSensitivity",
                table: "RootFolders",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "FailureKind",
                table: "MoveJobs",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<int>(
                name: "IdentityKeyVersion",
                table: "MoveJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "MoveJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "MoveJobs",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "MoveJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "MoveJobs",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<Guid>(
                name: "RelocationId",
                table: "MoveJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MoveJobEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MoveJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    EntryType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Length = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CopyState = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CleanupState = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoveJobEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoveJobEntries_MoveJobs_MoveJobId",
                        column: x => x.MoveJobId,
                        principalTable: "MoveJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RootFolderRelocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RootFolderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveRootFolderId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    TargetPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    DeleteEmptySource = table.Column<bool>(type: "INTEGER", nullable: false),
                    DesiredName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DesiredIsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    TargetCaseSensitivityMode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TotalJobs = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedJobs = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RootFolderRelocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RootFolderRelocations_RootFolders_RootFolderId",
                        column: x => x.RootFolderId,
                        principalTable: "RootFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RootFolders_PathIdentityKey",
                table: "RootFolders",
                column: "PathIdentityKey",
                unique: true,
                filter: "\"PathIdentityKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MoveJobs_RelocationId",
                table: "MoveJobs",
                column: "RelocationId");

            migrationBuilder.CreateIndex(
                name: "IX_MoveJobs_Status_NextAttemptAt_LeaseExpiresAt",
                table: "MoveJobs",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MoveJobEntries_MoveJobId_RelativePath",
                table: "MoveJobEntries",
                columns: new[] { "MoveJobId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RootFolderRelocations_ActiveRootFolderId",
                table: "RootFolderRelocations",
                column: "ActiveRootFolderId",
                unique: true,
                filter: "\"ActiveRootFolderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RootFolderRelocations_RootFolderId",
                table: "RootFolderRelocations",
                column: "RootFolderId");

            migrationBuilder.AddForeignKey(
                name: "FK_MoveJobs_RootFolderRelocations_RelocationId",
                table: "MoveJobs",
                column: "RelocationId",
                principalTable: "RootFolderRelocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MoveJobs_RootFolderRelocations_RelocationId",
                table: "MoveJobs");

            migrationBuilder.DropTable(
                name: "MoveJobEntries");

            migrationBuilder.DropTable(
                name: "RootFolderRelocations");

            migrationBuilder.DropIndex(
                name: "IX_RootFolders_PathIdentityKey",
                table: "RootFolders");

            migrationBuilder.DropIndex(
                name: "IX_MoveJobs_RelocationId",
                table: "MoveJobs");

            migrationBuilder.DropIndex(
                name: "IX_MoveJobs_Status_NextAttemptAt_LeaseExpiresAt",
                table: "MoveJobs");

            migrationBuilder.DropColumn(
                name: "CaseSensitivityMode",
                table: "RootFolders");

            migrationBuilder.DropColumn(
                name: "PathIdentityKey",
                table: "RootFolders");

            migrationBuilder.DropColumn(
                name: "PathIdentityState",
                table: "RootFolders");

            migrationBuilder.DropColumn(
                name: "ResolvedCaseSensitivity",
                table: "RootFolders");

            migrationBuilder.DropColumn(
                name: "FailureKind",
                table: "MoveJobs");

            migrationBuilder.DropColumn(
                name: "IdentityKeyVersion",
                table: "MoveJobs");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "MoveJobs");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "MoveJobs");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "MoveJobs");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "MoveJobs");

            migrationBuilder.DropColumn(
                name: "RelocationId",
                table: "MoveJobs");
        }
    }
}
