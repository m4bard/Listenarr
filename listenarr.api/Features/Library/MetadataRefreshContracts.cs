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

namespace Listenarr.Api.Features.Library;

/// <summary>
/// What to refresh. A null <paramref name="AuthorId"/> is the whole library; an id names a row
/// in MonitoredAuthors and refreshes that author's books.
/// </summary>
/// <param name="AuthorId">MonitoredAuthors row id, or null for the whole library.</param>
/// <param name="Force">Take every book in scope, ignoring the staleness age.</param>
public sealed record MetadataRefreshRequest(int? AuthorId = null, bool Force = false);

/// <summary>The run a trigger started, or the run that refused it.</summary>
public sealed record MetadataRefreshRunResponse(
    Guid RunId,
    string Scope,
    int TotalBooks,
    string Status);

/// <summary>A run's progress, mirroring the scan and move status endpoints.</summary>
public sealed record MetadataRefreshRunStatusResponse(
    Guid RunId,
    string Scope,
    string Status,
    int TotalBooks,
    int Processed,
    int Updated,
    int Skipped,
    int Deferred,
    int Failed,
    int RequestsSpent,
    DateTime StartedAt,
    DateTime? CompletedAt);
