using Listenarr.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Models.Configurations
{
    public class MonitoredAuthorConfiguration : IEntityTypeConfiguration<MonitoredAuthor>
    {
        public void Configure(EntityTypeBuilder<MonitoredAuthor> builder)
        {
            builder.HasKey(author => author.Id);

            builder.Property(author => author.AuthorName)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(author => author.AuthorNameNormalized)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(author => author.AuthorAsin)
                .HasMaxLength(32);

            builder.Property(author => author.Region)
                .IsRequired()
                .HasMaxLength(16);

            builder.Property(author => author.Language)
                .IsRequired()
                .HasMaxLength(32);

            builder.Property(author => author.LastError)
                .HasMaxLength(2048);
        }
    }
}
