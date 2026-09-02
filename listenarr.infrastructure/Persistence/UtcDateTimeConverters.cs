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
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Listenarr.Infrastructure.Persistence;

/// <summary>
/// Keeps a DateTime that went into the database as UTC coming back as UTC.
///
/// SQLite stores a DateTime as text with no offset, for example
/// "2026-09-02 18:07:56.7304844". Nothing in that text says which zone it is, so the value
/// materialises with Kind=Unspecified no matter what Kind it had when it was written. Every
/// DateTime column in this model is affected; the timestamps happen to be written as UTC, and
/// the reader has no way to know that.
///
/// This is not hypothetical. AudiobookFile.ApplyPhysicalObjectIdentity refuses a Kind that is
/// not Utc, and AudiobookFileService clones a loaded file by handing that same timestamp back
/// to it, so a rescan of an already registered file throws ArgumentException on a value the
/// application itself produced.
///
/// The conversion is on the read side, so rows already written come back correct without a
/// backfill. The stored text is unchanged, which means this does not need a migration and can
/// be reverted without one either.
/// </summary>
internal static class UtcDateTimeConverters
{
    internal static readonly ValueConverter<DateTime, DateTime> Value = new(
        write => write.Kind == DateTimeKind.Local
            ? write.ToUniversalTime()
            : DateTime.SpecifyKind(write, DateTimeKind.Utc),
        read => DateTime.SpecifyKind(read, DateTimeKind.Utc));

    internal static readonly ValueConverter<DateTime?, DateTime?> Nullable = new(
        write => write.HasValue
            ? (write.Value.Kind == DateTimeKind.Local
                ? write.Value.ToUniversalTime()
                : DateTime.SpecifyKind(write.Value, DateTimeKind.Utc))
            : write,
        read => read.HasValue
            ? DateTime.SpecifyKind(read.Value, DateTimeKind.Utc)
            : read);

    /// <summary>
    /// Applies the converters to every DateTime property that does not already have one.
    ///
    /// Written as one call rather than inline, so that removing it removes the whole fix. An
    /// earlier attempt at this used an if/else-if over the two CLR types, and disabling the
    /// first arm to test the second left the nullable arm running, which made the control
    /// pass whether or not the fix was present.
    /// </summary>
    internal static void ApplyTo(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.GetValueConverter() != null)
                {
                    continue;
                }

                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(Value);
                }

                if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(Nullable);
                }
            }
        }
    }
}
