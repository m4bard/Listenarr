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

namespace Listenarr.Infrastructure.Search.RequestBudget;

/// <summary>
/// The SQL behind the daily request allowance that has to be one statement.
/// </summary>
internal static class IndexerRequestBudgetSql
{
    /// <summary>
    /// Charges one request to the indexer's bucket for <paramref name="hourStartUtc"/> only
    /// while the indexer's count since <paramref name="windowStartUtc"/> is below
    /// <paramref name="ceiling"/>. The sum and the insert or increment are one statement, so
    /// SQLite holds the write lock across both and two callers can never both take the last
    /// unit. One row affected means charged; none means refused. The hours are bound as
    /// <see cref="DateTime"/> so they are written in the same text form EF writes for the key.
    /// </summary>
    public static FormattableString ChargeIfBelowCeiling(
        int indexerId,
        DateTime hourStartUtc,
        DateTime windowStartUtc,
        int ceiling) =>
        $"""
        INSERT INTO "IndexerRequestUsages" ("IndexerId", "HourStartUtc", "Count")
        SELECT {indexerId}, {hourStartUtc}, 1
        WHERE (SELECT COALESCE(SUM("Count"), 0) FROM "IndexerRequestUsages"
               WHERE "IndexerId" = {indexerId} AND "HourStartUtc" >= {windowStartUtc}) < {ceiling}
        ON CONFLICT ("IndexerId", "HourStartUtc") DO UPDATE SET "Count" = "Count" + 1
        """;
}
