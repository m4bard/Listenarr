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
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence;

/// <summary>
/// Runs at startup after the migrations. The AddAutomaticSearchSchedule migration is a pure EF
/// scaffold, so every book that existed before it holds the scaffolded due time
/// (<see cref="DateTime.MinValue"/>) and would be due at once. This gives each such book the due
/// time its last search and the configured cooldown imply, or now when it was never searched or
/// its last search time cannot be read. Only rows still at the scaffolded value are written, so a
/// second run changes nothing, and each set-based update covers at most one chunk of books.
/// </summary>
internal static class AutomaticSearchScheduleStartupRepair
{
    internal const string AutomaticSearchScheduleMigrationId =
        "20261114021916_AddAutomaticSearchSchedule";
    internal const int DefaultChunkSize = 500;

    public static int SetScaffoldedDueTimes(ListenArrDbContext context, DateTime nowUtc, int chunkSize = DefaultChunkSize)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        if (!context.Database.GetAppliedMigrations().Contains(AutomaticSearchScheduleMigrationId, StringComparer.Ordinal))
        {
            return 0;
        }

        var cooldownHours = ReadCooldown(context).TotalHours;
        var scaffoldedDueUtc = DateTime.MinValue;
        var updated = 0;
        var afterId = 0;
        while (true)
        {
            var chunkIds = context.Audiobooks
                .AsNoTracking()
                .Where(book => book.Id > afterId && book.AutomaticSearchDueUtc == scaffoldedDueUtc)
                .OrderBy(book => book.Id)
                .Select(book => book.Id)
                .Take(chunkSize)
                .ToList();
            if (chunkIds.Count == 0)
            {
                return updated;
            }

            var lastId = chunkIds[^1];
            // SQLite's date arithmetic yields NULL for a missing or unreadable last search time;
            // the coalesce turns both into "due now" instead of failing the NOT NULL column.
            updated += context.Audiobooks
                .Where(book => book.Id > afterId
                    && book.Id <= lastId
                    && book.AutomaticSearchDueUtc == scaffoldedDueUtc)
                .ExecuteUpdate(setters => setters.SetProperty(
                    book => book.AutomaticSearchDueUtc,
                    book => (DateTime?)book.LastSearchTime!.Value.AddHours(cooldownHours) ?? nowUtc));
            afterId = lastId;
        }
    }

    // The cooldown the sweep would use, with its clamp; the default when the settings row is
    // absent or its stored value is not an integer.
    private static TimeSpan ReadCooldown(ListenArrDbContext context)
    {
        var storedHours = context.ApplicationSettings
            .AsNoTracking()
            .Where(settings => settings.Id == 1)
            .Select(settings => settings.AutomaticSearchCooldownHours.ToString())
            .FirstOrDefault();
        var settings = new ApplicationSettings();
        if (int.TryParse(storedHours, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours))
        {
            settings.AutomaticSearchCooldownHours = hours;
        }

        return AutomaticSearchTiming.FromSettings(settings).BookCooldown;
    }
}
