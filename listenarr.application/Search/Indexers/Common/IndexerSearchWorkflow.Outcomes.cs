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

using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Search.Indexers.Common;

public partial class IndexerSearchWorkflow
{
    /// <summary>
    /// Feeds one indexer's answer to the failure backoff. A status write must never be able to fail
    /// a search that otherwise worked, so a persistence error is logged and swallowed here.
    /// </summary>
    private async Task RecordOutcomeAsync(Indexer indexer, IndexerQueryObservation observation, CancellationToken ct = default)
    {
        if (_indexerStatusService == null)
        {
            return;
        }

        try
        {
            await _indexerStatusService.RecordAsync(indexer, observation, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogWarning(ex, "Failed to record failure backoff state for indexer {Name}", indexer.Name);
        }
    }

    /// <summary>
    /// One line per indexer per search, naming how the indexer answered. Without it, a 500 and a
    /// genuine zero-match are indistinguishable in the log stream as well as in the return value.
    /// </summary>
    private void LogIndexerQueryOutcome(Indexer indexer, IndexerQueryObservation observation)
    {
        const string template =
            "Indexer {Name} query outcome {Outcome} ({Reason}) at tier {Tier} with {Count} results{Detail}";
        var detail = string.IsNullOrWhiteSpace(observation.Detail)
            ? string.Empty
            : $": {observation.Detail}";

        if (observation.Outcome is IndexerQueryOutcome.Unavailable
            or IndexerQueryOutcome.Unreadable
            or IndexerQueryOutcome.NotConfigured)
        {
            _logger.LogWarning(
                template,
                indexer.Name,
                observation.Outcome,
                observation.Reason,
                observation.Tier,
                observation.Results.Count,
                detail);
            return;
        }

        _logger.LogInformation(
            template,
            indexer.Name,
            observation.Outcome,
            observation.Reason,
            observation.Tier,
            observation.Results.Count,
            detail);
    }
}
