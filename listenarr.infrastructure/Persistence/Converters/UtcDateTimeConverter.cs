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
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Listenarr.Infrastructure.Persistence.Converters
{
    /// <summary>
    /// Keeps a timestamp that was stored as UTC coming back as UTC.
    ///
    /// SQLite has no datetime type, so a DateTime is stored as text without a zone
    /// marker, for example "2026-08-24 13:41:27.4246796". The text does not say which
    /// zone it is, so the value materialises with Kind=Unspecified regardless of the
    /// Kind it had when it was written. Use this on a column whose stored value is
    /// UTC by construction and whose consumer cares about Kind.
    ///
    /// The conversion happens on read, so rows already in the database come back
    /// correct with no backfill, and the stored text is unchanged, so applying or
    /// removing it needs no migration.
    /// </summary>
    public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                write => write.Kind == DateTimeKind.Local
                    ? write.ToUniversalTime()
                    : DateTime.SpecifyKind(write, DateTimeKind.Utc),
                read => DateTime.SpecifyKind(read, DateTimeKind.Utc))
        {
        }
    }
}
