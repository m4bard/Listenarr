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

namespace Listenarr.Application.Search.Scheduling;

/// <summary>
/// How one indexer's search for a book is recorded on its pair row.
/// </summary>
public enum PairScheduleResult
{
    /// <summary>Completed and the book was satisfied: both counts reset, next attempt after the cooldown.</summary>
    Satisfied,

    /// <summary>Completed with nothing acceptable: one more no-result, next attempt on the no-result ladder.</summary>
    NoAcceptableResults,

    /// <summary>
    /// Completed, and an acceptable release was found but not queued: counts unchanged, next attempt
    /// at the book's new due time (<see cref="PairScheduleUpdate.NotBeforeUtc"/>).
    /// </summary>
    GrabNotCompleted,

    /// <summary>
    /// Failed: one more failure, next attempt on the pair failure ladder, and never before a deferral's
    /// until-time when one came with the failure (<see cref="PairScheduleUpdate.NotBeforeUtc"/>).
    /// </summary>
    Failed,

    /// <summary>Deferred: counts unchanged, next attempt at the deferral's until-time.</summary>
    Deferred
}

/// <summary>
/// One pair's outcome to persist. <paramref name="NotBeforeUtc"/> is required for
/// <see cref="PairScheduleResult.GrabNotCompleted"/> and <see cref="PairScheduleResult.Deferred"/>,
/// optional for <see cref="PairScheduleResult.Failed"/> and ignored otherwise.
/// </summary>
public sealed record PairScheduleUpdate(
    int AudiobookId,
    int IndexerId,
    PairScheduleResult Result,
    DateTime? NotBeforeUtc = null);

/// <summary>
/// One book's outcome to persist. <paramref name="AnyPairCompleted"/> stamps the book's last search
/// time. <paramref name="NotBeforeUtc"/> is read only for
/// <see cref="BookSearchOutcome.GrabNotCompleted"/>: the until-time of the gate deferral that stopped
/// the grab, which then sets the due time without counting a book failure.
/// </summary>
public sealed record BookScheduleUpdate(
    int AudiobookId,
    BookSearchOutcome Outcome,
    bool AnyPairCompleted,
    DateTime? NotBeforeUtc = null);
