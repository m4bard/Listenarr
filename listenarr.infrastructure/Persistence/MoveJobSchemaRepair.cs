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

using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence;

/// <summary>
/// Performs narrowly-scoped SQLite schema repairs for known released-schema drift.
/// </summary>
internal static class MoveJobSchemaRepair
{
    private const string MoveJobsTable = "MoveJobs";
    private const string SourcePathColumn = "SourcePath";

    private static readonly string[] RequiredMoveJobColumns =
    [
        "Id",
        "AudiobookId",
        "RequestedPath",
        SourcePathColumn,
        "ActiveDeduplicationKey",
        "Status",
        "Error",
        "AttemptCount",
        "EnqueuedAt",
        "UpdatedAt"
    ];

    /// <summary>
    /// Adds the nullable MoveJobs.SourcePath column when an existing SQLite database missed the migration.
    /// </summary>
    /// <remarks>
    /// This is intentionally not a generic migration replacement. It only repairs the concrete drift that
    /// prevents move-job enqueue from querying persisted jobs before a physical library move starts.
    /// </remarks>
    public static bool EnsureSourcePathColumn(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return WithOpenSqliteConnection(dbContext, false, connection =>
        {
            if (!TableExists(connection) || ColumnExists(connection, SourcePathColumn))
            {
                return false;
            }

            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE \"MoveJobs\" ADD COLUMN \"SourcePath\" TEXT";
            command.ExecuteNonQuery();
            return true;
        });
    }

    /// <summary>
    /// Returns required MoveJobs columns that are still absent after migrations and targeted repair.
    /// </summary>
    public static IReadOnlyCollection<string> GetMissingMoveJobColumns(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return WithOpenSqliteConnection<IReadOnlyCollection<string>>(dbContext, Array.Empty<string>(), connection =>
        {
            if (!TableExists(connection))
            {
                return RequiredMoveJobColumns;
            }

            var existingColumns = GetColumnNames(connection);
            return RequiredMoveJobColumns
                .Where(column => !existingColumns.Contains(column))
                .ToArray();
        });
    }

    private static T WithOpenSqliteConnection<T>(
        DbContext dbContext,
        T fallbackValue,
        Func<SqliteConnection, T> action)
    {
        if (dbContext.Database.GetDbConnection() is not SqliteConnection connection)
        {
            return fallbackValue;
        }

        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            return action(connection);
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static bool TableExists(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $tableName LIMIT 1";
        command.Parameters.AddWithValue("$tableName", MoveJobsTable);
        return command.ExecuteScalar() is not null;
    }

    private static bool ColumnExists(SqliteConnection connection, string columnName) =>
        GetColumnNames(connection).Contains(columnName);

    private static HashSet<string> GetColumnNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(\"MoveJobs\")";

        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        return columns;
    }
}
