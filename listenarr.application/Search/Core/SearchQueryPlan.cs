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

namespace Listenarr.Application.Search.Core;

/// <summary>
/// Which parts of an audiobook record a query form is built from.
/// </summary>
public enum SearchQueryFormKind
{
    /// <summary>Query title followed by the first author.</summary>
    TitleAuthor,

    /// <summary>Query title alone.</summary>
    Title,

    /// <summary>The part of the title before its first subtitle delimiter, followed by the first author.</summary>
    TitleStemAuthor,

    /// <summary>Series name followed by the first author.</summary>
    SeriesAuthor,

    /// <summary>Text an operator typed, passed through untouched.</summary>
    Verbatim
}

/// <summary>
/// One query string and its position in the plan that produced it.
/// </summary>
/// <param name="Tier">1-based position in the plan. Tier 1 is issued first.</param>
/// <param name="Query">The query string, before <see cref="Indexers.Common.IndexerQuerySanitizer"/> sees it.</param>
/// <param name="Kind">Which parts of the record this form was built from.</param>
public sealed record SearchQueryForm(int Tier, string Query, SearchQueryFormKind Kind);

/// <summary>
/// The ordered list of query forms to try against one indexer, narrowest first.
/// </summary>
/// <remarks>
/// The plan exists because by the time a search reaches the indexer fan-out the audiobook has
/// been flattened to a single string and title, author and series are no longer recoverable.
/// Anything that wants to try a second form of the same search has to be handed the forms from
/// the point where the record still exists.
/// </remarks>
public sealed record SearchQueryPlan(IReadOnlyList<SearchQueryForm> Forms)
{
    /// <summary>
    /// A one-tier plan carrying exactly the text that was handed in.
    /// </summary>
    /// <remarks>
    /// Free-text search gets this. The operator typed a string and there is no structure to build
    /// further forms from; re-parsing their text into tiers would be guessing at what they meant.
    /// It also bounds the ladder's blast radius: escalation only ever happens on the two paths
    /// that start from a stored audiobook record.
    /// </remarks>
    public static SearchQueryPlan Verbatim(string query)
    {
        return new SearchQueryPlan(new[]
        {
            new SearchQueryForm(1, query ?? string.Empty, SearchQueryFormKind.Verbatim)
        });
    }

    /// <summary>
    /// Builds a plan from candidate forms, dropping blanks and repeats and numbering what survives.
    /// </summary>
    /// <remarks>
    /// Repeats are real rather than hypothetical: a book with no author makes the title-and-author
    /// form identical to the title-alone form, and issuing the same string twice would double the
    /// request cost for nothing.
    /// </remarks>
    internal static SearchQueryPlan FromCandidates(IEnumerable<(string Query, SearchQueryFormKind Kind)> candidates)
    {
        var forms = new List<SearchQueryForm>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (query, kind) in candidates)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                continue;
            }

            var trimmed = query.Trim();
            if (!seen.Add(trimmed))
            {
                continue;
            }

            forms.Add(new SearchQueryForm(forms.Count + 1, trimmed, kind));
        }

        return new SearchQueryPlan(forms);
    }

    /// <summary>Whether this plan is a single pass-through of operator-typed text.</summary>
    public bool IsVerbatim => Forms.Count == 1 && Forms[0].Kind == SearchQueryFormKind.Verbatim;

    /// <summary>
    /// The tier 1 query: what a caller that can only carry one string should send, and what the
    /// search would have sent before the ladder existed.
    /// </summary>
    public string PrimaryQuery => Forms.Count > 0 ? Forms[0].Query : string.Empty;
}
