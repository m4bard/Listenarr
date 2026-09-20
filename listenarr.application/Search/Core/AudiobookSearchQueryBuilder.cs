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

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Listenarr.Application.Search.Core;

/// <summary>
/// Turns an audiobook record into the ordered query forms handed to indexers.
/// </summary>
/// <remarks>
/// This is the single place an audiobook becomes indexer queries. Both the automatic sweep and
/// the download path call it, so the two cannot describe the same audiobook differently. The
/// stored <see cref="Audiobook.Title"/> stays untouched for display; what goes on the wire is
/// the derived query title from <see cref="BuildQueryTitle"/>.
/// </remarks>
public static class AudiobookSearchQueryBuilder
{
    /// <summary>
    /// Edition and format annotations that metadata providers append to a title.
    /// </summary>
    /// <remarks>
    /// Everything here describes how a recording was produced, never which work it is,
    /// so removing it cannot make two different audiobooks look alike. Annotations that
    /// do disambiguate, such as a part or volume number, are deliberately absent: losing
    /// those would turn a search for one half of a work into a search for either half.
    /// The list is closed rather than a pattern because a heuristic that guesses at
    /// annotations will eventually eat a real title.
    /// </remarks>
    private static readonly IReadOnlySet<string> EditionAnnotations =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "unabridged",
            "abridged",
            "unabridged edition",
            "abridged edition",
            "dramatized",
            "dramatised",
            "dramatized adaptation",
            "dramatised adaptation",
            "audio drama"
        };

    /// <summary>
    /// A parenthesised or bracketed span, captured without its delimiters.
    /// </summary>
    private static readonly Regex DelimitedSpan = new(
        @"\s*[\(\[]([^\(\)\[\]]*)[\)\]]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RepeatedWhitespace = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A single letter followed by a full stop, as author initials are usually written.
    /// </summary>
    private static readonly Regex Initial = new(
        @"(?<![\p{L}\p{N}])(\p{L})\.",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Builds the ordered query forms to try for one audiobook, narrowest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order matters more than the contents. Tier 1 is what the search sent before the ladder
    /// existed, so a book that was already being found costs exactly one request per indexer as
    /// it did before. Everything after tier 1 only happens when an indexer answered and said it
    /// had nothing.
    /// </para>
    /// <para>
    /// Series forms are additional rungs rather than a replacement for any title form: a real
    /// library has plenty of books with no series at all, and those must still get the title
    /// forms. They are skipped entirely when the title already says the series as whole words,
    /// because "Oz L. Frank Baum" after "The Wonderful Wizard of Oz L. Frank Baum" asks a
    /// strictly broader version of a question already answered.
    /// </para>
    /// <para>
    /// A title's subtitle is demoted, never discarded. Readarr keeps the part before the colon
    /// and throws the rest away, which turns "Sherlock Holmes: A Study in Scarlet" into a search
    /// for the series and "She: A History of Adventure" into a search for the word "She". Here
    /// the full title stays at tier 1 and the shortened form is an extra rung further down, so a
    /// bad guess about which half carries the work costs a later request rather than the search.
    /// </para>
    /// <para>
    /// A rung once searched the series alone, with no author, on the reasoning that the widest
    /// possible query belonged at the bottom of the ladder. It was wrong for the same reason a
    /// bare title stem is: a short, generic-enough name can be right about which book it hits and
    /// wrong about which record entirely. On a live install that rung searched a two-word series
    /// name that is also a band name and the grab went to a music artist. The rung is gone;
    /// <see cref="SearchQueryFormKind.SeriesAuthor"/> still recovers a book only the series
    /// carries, just never without the author anchor.
    /// </para>
    /// </remarks>
    public static SearchQueryPlan BuildPlan(Audiobook audiobook)
    {
        ArgumentNullException.ThrowIfNull(audiobook);

        var queryTitle = BuildQueryTitle(audiobook.Title);
        var author = BuildQueryAuthor(audiobook.Authors);
        var series = Collapse(audiobook.Series ?? string.Empty);
        var titleStem = BuildTitleStem(queryTitle);

        // A series the title already spells out adds nothing to a query the title already carries.
        if (series.Length > 0 && ContainsPhrase(queryTitle, series))
        {
            series = string.Empty;
        }

        var candidates = new List<(string Query, SearchQueryFormKind Kind)>
        {
            (Join(queryTitle, author), SearchQueryFormKind.TitleAuthor),
            (queryTitle, SearchQueryFormKind.Title),

            // Only ever paired with the author. Alone, a stem such as "She" is broad enough to be
            // noise, the same reason the series is never issued without the author either.
            (Join(titleStem, author), SearchQueryFormKind.TitleStemAuthor),

            (Join(series, author), SearchQueryFormKind.SeriesAuthor)
        };

        var plan = SearchQueryPlan.FromCandidates(candidates);

        // A record with nothing usable in it still has to produce a query, or the search silently
        // stops asking rather than asking badly.
        return plan.Forms.Count > 0
            ? plan
            : SearchQueryPlan.Verbatim(Collapse(audiobook.Title ?? string.Empty));
    }

    /// <summary>
    /// Derives the query form of a stored display title.
    /// </summary>
    /// <remarks>
    /// Only delimited spans whose entire content is a known edition annotation are
    /// removed. A parenthesised span that is anything else is left alone, because it is
    /// far more likely to be part of the work's name than an artefact of the metadata
    /// provider, and a query missing real title words finds nothing at all.
    /// </remarks>
    public static string BuildQueryTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var stripped = DelimitedSpan.Replace(title, match =>
            EditionAnnotations.Contains(Collapse(match.Groups[1].Value))
                ? " "
                : match.Value);

        stripped = Collapse(stripped).Trim(' ', ',', ';', ':', '-');

        // Stripping must never empty a title. If the annotation was the whole thing,
        // the stored title is a better query than nothing.
        return stripped.Length == 0 ? Collapse(title) : stripped;
    }

    /// <summary>
    /// The first usable author name, with initials written the way an indexer tokenises them.
    /// </summary>
    /// <remarks>
    /// The sanitizer does not strip a full stop, so "Dennis E. Taylor" reaches the wire with the
    /// period attached to the initial and an indexer that splits on whitespace is asked for the
    /// token "E." rather than "E". Normalising here rather than in the sanitizer keeps an
    /// operator's typed text intact on the free-text path, which shares the sanitizer.
    /// </remarks>
    internal static string BuildQueryAuthor(IEnumerable<string>? authors)
    {
        var author = authors?.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        return string.IsNullOrWhiteSpace(author)
            ? string.Empty
            : Collapse(Initial.Replace(author, "$1 "));
    }

    /// <summary>
    /// The part of a title before its subtitle, or nothing when the title carries no subtitle.
    /// </summary>
    internal static string BuildTitleStem(string queryTitle)
    {
        var delimiter = queryTitle.IndexOf(':');
        if (delimiter <= 0)
        {
            return string.Empty;
        }

        var stem = Collapse(queryTitle[..delimiter]);
        return string.Equals(stem, queryTitle, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : stem;
    }

    /// <summary>
    /// Reports whether <paramref name="phrase"/> occurs in <paramref name="text"/> as a
    /// run of whole words, ignoring case, accents and punctuation.
    /// </summary>
    /// <remarks>
    /// Word runs rather than raw substrings, so the series "Oz" is found in "The
    /// Wonderful Wizard of Oz" but not in a title that merely mentions "Ozymandias".
    /// </remarks>
    internal static bool ContainsPhrase(string? text, string? phrase)
    {
        var haystack = Tokenize(text);
        var needle = Tokenize(phrase);

        if (needle.Count == 0 || needle.Count > haystack.Count)
        {
            return false;
        }

        for (var start = 0; start <= haystack.Count - needle.Count; start++)
        {
            var matched = true;
            for (var offset = 0; offset < needle.Count; offset++)
            {
                if (!string.Equals(haystack[start + offset], needle[offset], StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static string Join(string left, string right)
    {
        if (left.Length == 0)
        {
            return string.Empty;
        }

        return right.Length == 0 ? left : left + " " + right;
    }

    private static List<string> Tokenize(string? value)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return tokens;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // An apostrophe joins a word rather than breaking it, so "Alice's" is one
            // token and matches a series recorded as "Alices". Readarr treats the same
            // characters as word characters in SearchCriteriaBase.GetQueryTitle.
            if (ch is '\'' or '’' or 'ʼ' or '`' or '´')
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        foreach (var token in builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            tokens.Add(token);
        }

        return tokens;
    }

    private static string Collapse(string value)
    {
        return RepeatedWhitespace.Replace(value, " ").Trim();
    }
}
