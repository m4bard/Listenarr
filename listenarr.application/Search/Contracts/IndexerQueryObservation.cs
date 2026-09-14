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
/// Coarse result of asking one indexer one query. Distinguishes "the indexer answered and had
/// nothing" from "the indexer did not answer", which a bare result list cannot express.
/// </summary>
public enum IndexerQueryOutcome
{
    /// <summary>Readable answer carrying one or more results.</summary>
    Hit,

    /// <summary>Readable answer carrying zero results: the indexer said it does not have it.</summary>
    NoMatch,

    /// <summary>No usable answer: non-2xx status, timeout, or network exception.</summary>
    Unavailable,

    /// <summary>Answered 2xx, but the body could not be parsed.</summary>
    Unreadable,

    /// <summary>No provider resolved for this indexer's implementation, or the indexer lacks credentials.</summary>
    NotConfigured
}

/// <summary>
/// Fine-grained cause behind an <see cref="IndexerQueryOutcome"/>.
/// </summary>
public enum IndexerQueryReason
{
    None,
    HttpStatus,
    Timeout,
    NetworkError,
    Cancelled,
    MalformedXml,
    MissingChannel,

    /// <summary>A well-formed answer that carried no entries.</summary>
    EmptyChannel,
    NoProviderForImplementation
}

/// <summary>
/// One indexer's answer to one query: the results, plus why there were that many of them.
/// </summary>
/// <param name="Outcome">Coarse classification of the answer.</param>
/// <param name="Reason">Fine-grained cause behind <paramref name="Outcome"/>.</param>
/// <param name="Results">Results parsed from the answer; empty for everything but <see cref="IndexerQueryOutcome.Hit"/>.</param>
/// <param name="Tier">1-based position of this query in the <see cref="Core.SearchQueryPlan"/> that produced it.</param>
/// <param name="QueryForm">The query string that was issued, for logs and diagnostics.</param>
/// <param name="Detail">Status code or exception type for the log line. Never a URL or an API key.</param>
public sealed record IndexerQueryObservation(
    IndexerQueryOutcome Outcome,
    IndexerQueryReason Reason,
    IReadOnlyList<IndexerSearchResult> Results,
    int Tier,
    string? QueryForm,
    string? Detail = null)
{
    private static readonly IReadOnlyList<IndexerSearchResult> NoResults = Array.Empty<IndexerSearchResult>();

    /// <summary>
    /// Whether a query plan should try the next form. True only when the indexer actually answered and
    /// actually had nothing: escalating against an indexer that is timing out is what gets an install
    /// rate-limited or blocked.
    /// </summary>
    public bool ShouldEscalate => Outcome == IndexerQueryOutcome.NoMatch;

    /// <summary>Whether the indexer produced a readable answer at all.</summary>
    public bool Answered => Outcome is IndexerQueryOutcome.Hit or IndexerQueryOutcome.NoMatch;

    /// <summary>
    /// Classifies a readable answer by whether it carried anything.
    /// </summary>
    public static IndexerQueryObservation FromResults(
        IReadOnlyList<IndexerSearchResult> results,
        string? queryForm,
        int tier = 1)
    {
        return results.Count > 0
            ? new IndexerQueryObservation(IndexerQueryOutcome.Hit, IndexerQueryReason.None, results, tier, queryForm)
            : new IndexerQueryObservation(IndexerQueryOutcome.NoMatch, IndexerQueryReason.EmptyChannel, NoResults, tier, queryForm);
    }

    /// <summary>The indexer did not produce a usable answer.</summary>
    public static IndexerQueryObservation Unavailable(
        IndexerQueryReason reason,
        string? queryForm,
        string? detail = null,
        int tier = 1)
    {
        return new IndexerQueryObservation(IndexerQueryOutcome.Unavailable, reason, NoResults, tier, queryForm, detail);
    }

    /// <summary>The indexer answered, but the body could not be parsed.</summary>
    public static IndexerQueryObservation Unreadable(
        IndexerQueryReason reason,
        string? queryForm,
        string? detail = null,
        int tier = 1)
    {
        return new IndexerQueryObservation(IndexerQueryOutcome.Unreadable, reason, NoResults, tier, queryForm, detail);
    }

    /// <summary>The query was never issued because the indexer is not usable as configured.</summary>
    public static IndexerQueryObservation NotConfigured(
        IndexerQueryReason reason,
        string? queryForm,
        string? detail = null,
        int tier = 1)
    {
        return new IndexerQueryObservation(IndexerQueryOutcome.NotConfigured, reason, NoResults, tier, queryForm, detail);
    }

    /// <summary>The indexer answered and had nothing.</summary>
    public static IndexerQueryObservation NoMatch(
        IndexerQueryReason reason,
        string? queryForm,
        string? detail = null,
        int tier = 1)
    {
        return new IndexerQueryObservation(IndexerQueryOutcome.NoMatch, reason, NoResults, tier, queryForm, detail);
    }
}
