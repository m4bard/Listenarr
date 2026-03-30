using Listenarr.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Models.Configurations
{
    public class AudiobookSeriesMembershipConfiguration : IEntityTypeConfiguration<AudiobookSeriesMembership>
    {
        public void Configure(EntityTypeBuilder<AudiobookSeriesMembership> builder)
        {
            builder.ToTable("AudiobookSeriesMemberships");

            builder.Property(m => m.SeriesName)
                .HasMaxLength(512);

            builder.Property(m => m.SeriesNumber)
                .HasMaxLength(128);

            builder.Property(m => m.SeriesAsin)
                .HasMaxLength(64);

            builder.HasIndex(m => m.AudiobookId);
            builder.HasIndex(m => new { m.AudiobookId, m.SortOrder });
            builder.HasIndex(m => new { m.AudiobookId, m.IsPrimary });

            builder.HasOne(m => m.Audiobook)
                .WithMany(a => a.SeriesMemberships)
                .HasForeignKey(m => m.AudiobookId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
