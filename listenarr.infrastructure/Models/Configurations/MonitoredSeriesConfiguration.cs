using Listenarr.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Models.Configurations
{
    public class MonitoredSeriesConfiguration : IEntityTypeConfiguration<MonitoredSeries>
    {
        public void Configure(EntityTypeBuilder<MonitoredSeries> builder)
        {
            builder.HasKey(series => series.Id);

            builder.Property(series => series.SeriesName)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(series => series.SeriesNameNormalized)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(series => series.SeriesAsin)
                .HasMaxLength(32);

            builder.Property(series => series.Region)
                .IsRequired()
                .HasMaxLength(16);

            builder.Property(series => series.Language)
                .IsRequired()
                .HasMaxLength(32);

            builder.Property(series => series.LastError)
                .HasMaxLength(2048);
        }
    }
}
