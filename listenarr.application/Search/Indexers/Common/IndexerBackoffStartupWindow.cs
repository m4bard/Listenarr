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

namespace Listenarr.Application.Search.Indexers.Common;

/// <summary>
/// The first few minutes after the process starts, during which failure backoff is capped.
/// </summary>
/// <remarks>
/// A container that comes up before its network does, or before the Jackett instance it points at
/// does, will fail every indexer it has. Without a cap, one unlucky restart buries every indexer on
/// the install for hours, and the state is persisted, so it survives the restart that would
/// otherwise have cleared it. Registered as a singleton: it has to time from process start, and the
/// service that reads it is scoped.
/// </remarks>
public sealed class IndexerBackoffStartupWindow
{
    /// <summary>How long after start the cap applies.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);

    private readonly DateTimeOffset _startedAt;

    public IndexerBackoffStartupWindow(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _startedAt = timeProvider.GetUtcNow();
    }

    /// <summary>Whether <paramref name="now"/> still falls inside the window.</summary>
    public bool Contains(DateTimeOffset now) => now - _startedAt < Duration;
}
