using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileDurableMoveJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only reconciliation migration. EF correctly scaffolds no schema
            // operations here; these updates normalize existing MoveJob rows created
            // before durable move state tracking was introduced.
            migrationBuilder.Sql(
                "UPDATE \"MoveJobs\" SET \"Status\" = 'Running' WHERE \"Status\" = 'Processing';");
            migrationBuilder.Sql(
                "UPDATE \"MoveJobs\" SET \"IdentityKeyVersion\" = 1, " +
                "\"ActiveDeduplicationKey\" = 'legacy:' || \"Id\" " +
                "WHERE \"Status\" IN ('Queued', 'Running', 'RetryScheduled');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse only the data normalization performed above.
            migrationBuilder.Sql(
                "UPDATE \"MoveJobs\" SET \"Status\" = 'Processing' WHERE \"Status\" = 'Running';");
            migrationBuilder.Sql(
                "UPDATE \"MoveJobs\" SET \"ActiveDeduplicationKey\" = 'legacy:' || \"Id\" " +
                "WHERE \"Status\" IN ('Queued', 'Processing', 'RetryScheduled');");
        }
    }
}
