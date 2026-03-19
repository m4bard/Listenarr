using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Models.Configurations
{
    public class SeriesCacheEntryConfiguration : IEntityTypeConfiguration<SeriesCacheEntry>
    {
        public void Configure(EntityTypeBuilder<SeriesCacheEntry> builder)
        {
            builder.HasKey(entry => entry.Id);

            builder.Property(entry => entry.SeriesName)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(entry => entry.SeriesNameNormalized)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(entry => entry.SeriesAsin)
                .HasMaxLength(32);

            builder.Property(entry => entry.Region)
                .IsRequired()
                .HasMaxLength(16);

            builder.Property(entry => entry.ImageUrl)
                .HasMaxLength(2048);

            var catalogBooksConverter =
                (Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<CachedSeriesCatalogBook>?, string>)
                new JsonValueConverter<List<CachedSeriesCatalogBook>?>();
            var catalogBooksComparer = JsonValueComparer.Create<List<CachedSeriesCatalogBook>?>();
            var catalogBooksProp = builder.Property(entry => entry.CatalogBooks)
                .HasConversion(catalogBooksConverter)
                .HasColumnType("TEXT");
            catalogBooksProp.Metadata.SetValueComparer(catalogBooksComparer);

            builder.HasIndex(entry => new { entry.SeriesNameNormalized, entry.Region })
                .IsUnique();

            builder.HasIndex(entry => new { entry.SeriesAsin, entry.Region });
        }
    }
}
