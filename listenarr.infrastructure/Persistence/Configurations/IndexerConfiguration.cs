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
    /// <summary>
    /// The failure-backoff columns migration 20260914152223 added to Indexers, kept in the model as
    /// shadow properties now that nothing reads or writes them. Keeping them mapped leaves the
    /// model snapshot unchanged and the columns in place, so an older build that still runs the
    /// backoff can be put back on the same database.
    /// </summary>
    public class IndexerConfiguration : IEntityTypeConfiguration<Indexer>
    {
        public void Configure(EntityTypeBuilder<Indexer> builder)
        {
            builder.Property<DateTime?>("InitialFailure");
            builder.Property<DateTime?>("MostRecentFailure");
            builder.Property<int>("EscalationLevel");
            builder.Property<DateTime?>("DisabledTill");
            builder.Property<string?>("LastFailureReason");
        }
    }
}
