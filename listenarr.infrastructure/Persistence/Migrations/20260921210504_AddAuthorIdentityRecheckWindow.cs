using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorIdentityRecheckWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthorIdentityRepairRecheckAfterDays",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                // Zero is a meaningful value here, and it means "recheck everything on every
                // run", so the scaffolded default is not merely unhelpful: it is a different
                // behaviour, and the one the cutoff was added to stop happening by default.
                defaultValue: 30);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorIdentityRepairRecheckAfterDays",
                table: "ApplicationSettings");
        }
    }
}
