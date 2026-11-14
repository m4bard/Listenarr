using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Persistence.Configurations;

internal sealed class IndexerRequestStateConfiguration
    : IEntityTypeConfiguration<IndexerRequestState>
{
    public void Configure(EntityTypeBuilder<IndexerRequestState> builder)
    {
        builder.ToTable("IndexerRequestStates");
        // The key is the indexer's id, never generated here. As a plain INTEGER PRIMARY KEY it
        // is SQLite's rowid, so the per-indexer lookup and the upsert's conflict check are
        // rowid searches.
        builder.HasKey(state => state.IndexerId);
        builder.Property(state => state.IndexerId).ValueGeneratedNever();
        builder.Property(state => state.ConsecutiveFailures).HasDefaultValue(0);
        builder.Property(state => state.CooldownReason).HasMaxLength(32);
        builder.HasOne<Indexer>()
            .WithMany()
            .HasForeignKey(state => state.IndexerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
