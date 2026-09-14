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

namespace Listenarr.Application.Search.Contracts;

/// <summary>
/// What one query outcome should do to an indexer's failure backoff. Separate from
/// <see cref="IndexerQueryOutcome"/> because the two answer different questions: the outcome says
/// how the indexer answered this one query, the signal says how hard to back off from asking again.
/// </summary>
public enum IndexerBackoffSignal
{
    /// <summary>Nothing was learned about the indexer's availability; leave the state alone.</summary>
    Ignore,

    /// <summary>The indexer answered. Walk one rung back down.</summary>
    Success,

    /// <summary>The indexer did not answer for a reason that is plausibly its own. Climb one rung.</summary>
    Escalate,

    /// <summary>
    /// The indexer was unreachable at the transport. Re-block at the rung already held rather than
    /// climbing: DNS and connect failures usually mean our own side broke, and one dropped VPN or
    /// one dead proxy takes out every indexer behind it at once. Escalating all of them toward a
    /// day-long ceiling for a local outage is self-harm.
    /// </summary>
    Hold,

    /// <summary>
    /// The indexer stated a rate it will not exceed. Climb until the rung is at least as long as
    /// the number it gave.
    /// </summary>
    RateLimited
}

/// <summary>
/// Maps an indexer's answer to what the failure backoff should do about it, in one place, so the
/// polarity decisions live somewhere they can be read and tested rather than being spread across
/// the call sites that happen to observe each outcome.
/// </summary>
public static class IndexerBackoffPolicy
{
    /// <summary>
    /// Classifies one observation.
    /// </summary>
    public static IndexerBackoffSignal Classify(IndexerQueryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return Classify(observation.Outcome, observation.Reason);
    }

    /// <summary>
    /// Classifies one outcome and reason pair.
    /// </summary>
    public static IndexerBackoffSignal Classify(IndexerQueryOutcome outcome, IndexerQueryReason reason)
    {
        // A readable answer is a success whether or not it carried anything. "The indexer has
        // nothing" is the indexer working, and backing off from an indexer for answering honestly
        // is how a breaker ends up muting a healthy install.
        if (outcome is IndexerQueryOutcome.Hit or IndexerQueryOutcome.NoMatch)
        {
            return IndexerBackoffSignal.Success;
        }

        // No request left the process, so nothing was observed about the remote. A missing mam_id or
        // an implementation with no provider registered is a configuration fault the operator has to
        // fix; a cooldown on it would only delay the next attempt without telling anyone why.
        if (outcome == IndexerQueryOutcome.NotConfigured)
        {
            return IndexerBackoffSignal.Ignore;
        }

        return reason switch
        {
            // The caller asked to stop. That says nothing about the indexer.
            IndexerQueryReason.Cancelled => IndexerBackoffSignal.Ignore,
            IndexerQueryReason.RateLimited => IndexerBackoffSignal.RateLimited,
            IndexerQueryReason.NetworkError => IndexerBackoffSignal.Hold,
            _ => IndexerBackoffSignal.Escalate
        };
    }
}
