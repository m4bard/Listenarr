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
/// The request gate with no per-indexer state: no pacing, no cooldown, no persistence. It only
/// enforces the per-cycle request allowance carried in the request context.
/// </summary>
public sealed class PassThroughIndexerRequestGate : IIndexerRequestGate
{
    private readonly TimeProvider _timeProvider;

    public PassThroughIndexerRequestGate(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task<IndexerRequestResult> SendAsync(
        IndexerRequestContext context,
        Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dispatch);

        if (context.CycleAllowance is { } allowance && !allowance.TryTake())
        {
            return new IndexerRequestResult.Deferred(new RequestDeferral(
                _timeProvider.GetUtcNow().UtcDateTime,
                RequestDeferralReason.CycleRequestBound));
        }

        var response = await dispatch(cancellationToken);
        return new IndexerRequestResult.Sent(response);
    }

    public Task<RequestDeferral?> GetDeferralAsync(
        int indexerId,
        SearchOrigin origin,
        CancellationToken cancellationToken)
        => Task.FromResult<RequestDeferral?>(null);
}
