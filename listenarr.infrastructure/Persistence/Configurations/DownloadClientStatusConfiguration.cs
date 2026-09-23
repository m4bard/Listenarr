/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Persistence.Configurations
{
    public class DownloadClientStatusConfiguration : IEntityTypeConfiguration<DownloadClientStatus>
    {
        public void Configure(EntityTypeBuilder<DownloadClientStatus> builder)
        {
            builder.ToTable("DownloadClientStatuses");
            builder.HasKey(s => s.ClientId);

            // The row goes with its client. Readarr deletes it on ProviderDeletedEvent
            // (src/NzbDrone.Core/ThingiProvider/Status/ProviderStatusServiceBase.cs:152-155); a
            // cascading key does the same without an event bus, and also refuses a row for a
            // client that does not exist.
            builder.HasOne<DownloadClientConfiguration>()
                .WithMany()
                .HasForeignKey(s => s.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
