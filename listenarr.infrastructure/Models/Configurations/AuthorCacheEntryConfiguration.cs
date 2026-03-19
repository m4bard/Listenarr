using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Models.Configurations
{
    public class AuthorCacheEntryConfiguration : IEntityTypeConfiguration<AuthorCacheEntry>
    {
        public void Configure(EntityTypeBuilder<AuthorCacheEntry> builder)
        {
            builder.HasKey(entry => entry.Id);

            builder.Property(entry => entry.AuthorName)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(entry => entry.AuthorNameNormalized)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(entry => entry.AuthorAsin)
                .HasMaxLength(32);

            builder.Property(entry => entry.Region)
                .IsRequired()
                .HasMaxLength(16);

            builder.Property(entry => entry.ImageUrl)
                .HasMaxLength(2048);

            var similarAuthorsConverter =
                (Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<CachedRelatedAuthor>?, string>)
                new JsonValueConverter<List<CachedRelatedAuthor>?>();
            var similarAuthorsComparer = JsonValueComparer.Create<List<CachedRelatedAuthor>?>();
            var similarAuthorsProp = builder.Property(entry => entry.SimilarAuthors)
                .HasConversion(similarAuthorsConverter)
                .HasColumnType("TEXT");
            similarAuthorsProp.Metadata.SetValueComparer(similarAuthorsComparer);

            var catalogBooksConverter =
                (Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<CachedAuthorCatalogBook>?, string>)
                new JsonValueConverter<List<CachedAuthorCatalogBook>?>();
            var catalogBooksComparer = JsonValueComparer.Create<List<CachedAuthorCatalogBook>?>();
            var catalogBooksProp = builder.Property(entry => entry.CatalogBooks)
                .HasConversion(catalogBooksConverter)
                .HasColumnType("TEXT");
            catalogBooksProp.Metadata.SetValueComparer(catalogBooksComparer);

            builder.HasIndex(entry => new { entry.AuthorNameNormalized, entry.Region })
                .IsUnique();

            builder.HasIndex(entry => new { entry.AuthorAsin, entry.Region });
        }
    }
}
