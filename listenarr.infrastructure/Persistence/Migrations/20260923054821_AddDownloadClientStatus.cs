using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadClientStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadClientStatuses",
                columns: table => new
                {
                    ClientId = table.Column<string>(type: "TEXT", nullable: false),
                    InitialFailure = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MostRecentFailure = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EscalationLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    DisabledTill = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadClientStatuses", x => x.ClientId);
                    table.ForeignKey(
                        name: "FK_DownloadClientStatuses_DownloadClientConfigurations_ClientId",
                        column: x => x.ClientId,
                        principalTable: "DownloadClientConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadClientStatuses");
        }
    }
}
