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

using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Submission
{
    /// <summary>
    /// What <see cref="RequestedBookReleaseFilter.Evaluate"/> decided about one release.
    /// </summary>
    public enum RequestedBookMatch
    {
        /// <summary>Nothing about the release says it is a different book.</summary>
        Accepted,

        /// <summary>The release title does not carry the book's title words.</summary>
        TitleMismatch,

        /// <summary>The release names its own author, and it is not the book's author.</summary>
        AuthorMismatch,

        /// <summary>
        /// The title matched, but it is too short to identify the book by itself and the book's
        /// author appears nowhere on the release.
        /// </summary>
        AuthorNotCorroborated
    }

    /// <summary>
    /// Drops candidates that are plainly for a different book, before one of them is selected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scorer sees a release and a quality profile and nothing else, so on the paths that grab
    /// without a person choosing, the top-scored release used to win even when it was another
    /// work. An indexer's free-text search answers a short title with anything containing it, and
    /// a live install grabbed a different author's book whose title contained the requested book's
    /// one-word title.
    /// </para>
    /// <para>
    /// Readarr does not accept a release until its parser has found both the requested author and
    /// the requested book in the release name (Parser.ParseBookTitleWithSearchCriteria), and then
    /// rejects one whose parsed book is not the book being searched for
    /// (BookRequestedSpecification, "Book wasn't requested"). This is a narrower stopgap in the
    /// same direction. It only rejects what is clearly wrong, because a false rejection costs the
    /// grab of a correct release and the operator cannot see why.
    /// </para>
    /// <para>
    /// The rules, in order:
    /// every significant word of the book's title, or of the part before its subtitle, must appear
    /// in the release title as a whole word; case, accents, punctuation and extra words are
    /// ignored. The book's author then counts as corroborated when their surname appears in the
    /// release title or author field. When it does not, a release whose author field names
    /// somebody else is rejected, and a release that names nobody is accepted only when the
    /// matched title is at least <see cref="AudiobookSearchQueryBuilder.MinimumSignificantWordsToIssueAlone"/>
    /// significant words long, the same bar the query ladder uses for sending a title with no
    /// author. A book with no title or no author is judged on whatever it does have.
    /// </para>
    /// </remarks>
    public static class RequestedBookReleaseFilter
    {
        /// <summary>
        /// Words a book record's title can carry that describe the recording rather than the
        /// work, so a release that leaves them out is not missing any of the title.
        /// </summary>
        private static readonly IReadOnlySet<string> EditionWords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "unabridged", "abridged", "audiobook", "edition", "dramatized", "dramatised"
            };

        /// <summary>
        /// What parsers and metadata write when they have no author. A placeholder says nothing
        /// about who wrote the release, so it can neither corroborate nor contradict the book.
        /// </summary>
        private static readonly IReadOnlySet<string> PlaceholderAuthors =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "unknown", "unknown author", "various", "various authors", "various artists",
                "anonymous", "anon", "na", "n a"
            };

        /// <summary>
        /// Generational and honorific suffixes, which follow the surname and are not it.
        /// </summary>
        private static readonly IReadOnlySet<string> NameSuffixes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "jr", "sr", "ii", "iii", "iv", "phd", "md"
            };

        public static RequestedBookMatch Evaluate(Audiobook audiobook, SearchResult release)
        {
            ArgumentNullException.ThrowIfNull(audiobook);
            ArgumentNullException.ThrowIfNull(release);

            var titleForms = BuildTitleForms(audiobook.Title);
            if (titleForms.Count == 0)
            {
                // Nothing to compare, and this check exists to reject what is clearly wrong,
                // not to refuse what it cannot judge.
                return RequestedBookMatch.Accepted;
            }

            var releaseTitleTokens = AudiobookSearchQueryBuilder.Tokenize(release.Title);
            var releaseTitleWords = new HashSet<string>(releaseTitleTokens, StringComparer.Ordinal);

            // The longest form the release fully carries. "Frankenstein [m4b]" carries the stem of
            // "Frankenstein: or, The Modern Prometheus" and not the whole title, so it has shown
            // one word of identity, not three.
            var matchedWordCount = titleForms
                .Where(form => form.All(releaseTitleWords.Contains))
                .Select(form => form.Count)
                .DefaultIfEmpty(0)
                .Max();

            if (matchedWordCount == 0)
            {
                return RequestedBookMatch.TitleMismatch;
            }

            var surnames = BuildSurnameKeys(audiobook.Authors);
            if (surnames.Count == 0)
            {
                return RequestedBookMatch.Accepted;
            }

            var releaseAuthorTokens = IsPlaceholderAuthor(release.Artist)
                ? new List<string>()
                : AudiobookSearchQueryBuilder.Tokenize(release.Artist);

            var corroborated = surnames.Overlaps(WordsAndJoinedPairs(releaseTitleTokens))
                || surnames.Overlaps(WordsAndJoinedPairs(releaseAuthorTokens));
            if (corroborated)
            {
                return RequestedBookMatch.Accepted;
            }

            // An author field made entirely of words already in the title is not independent
            // information. The Torznab parser fills it with whatever precedes " - ", so for
            // "Title - Narrator" it holds the title, and reading that as the release's author
            // would reject the release for a disagreement it never made.
            var namesAnotherAuthor = releaseAuthorTokens.Count > 0
                && !releaseAuthorTokens.All(releaseTitleWords.Contains);
            if (namesAnotherAuthor)
            {
                return RequestedBookMatch.AuthorMismatch;
            }

            return matchedWordCount >= AudiobookSearchQueryBuilder.MinimumSignificantWordsToIssueAlone
                ? RequestedBookMatch.Accepted
                : RequestedBookMatch.AuthorNotCorroborated;
        }

        /// <summary>
        /// The candidates that pass <see cref="Evaluate"/>, in the order given. An empty result is
        /// the existing "no acceptable search results" answer at both callers, not an error.
        /// </summary>
        public static List<QualityScore> Exclude(Audiobook audiobook, List<QualityScore> scoredResults, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(audiobook);
            ArgumentNullException.ThrowIfNull(scoredResults);

            var kept = new List<QualityScore>(scoredResults.Count);
            foreach (var scored in scoredResults)
            {
                var verdict = Evaluate(audiobook, scored.SearchResult);
                if (verdict == RequestedBookMatch.Accepted)
                {
                    kept.Add(scored);
                    continue;
                }

                logger.LogInformation(
                    "Skipped release '{ReleaseTitle}' for audiobook {AudiobookId} '{Title}': {Reason}",
                    LogRedaction.SanitizeText(scored.SearchResult.Title),
                    audiobook.Id,
                    LogRedaction.SanitizeText(audiobook.Title),
                    Describe(verdict));
            }

            return kept;
        }

        private static string Describe(RequestedBookMatch verdict) => verdict switch
        {
            RequestedBookMatch.TitleMismatch => "release title does not contain the book's title",
            RequestedBookMatch.AuthorMismatch => "release names a different author",
            RequestedBookMatch.AuthorNotCorroborated => "title is too short to identify the book and the author is not on the release",
            _ => verdict.ToString()
        };

        /// <summary>
        /// The sets of words a release title has to carry, one per acceptable spelling of the
        /// book's title: as stored minus edition annotations, the part before the subtitle, and
        /// with every bracketed span removed. Forms with no significant words are left out.
        /// </summary>
        private static List<HashSet<string>> BuildTitleForms(string? title)
        {
            var forms = new List<HashSet<string>>();
            if (string.IsNullOrWhiteSpace(title))
            {
                return forms;
            }

            var queryTitle = AudiobookSearchQueryBuilder.BuildQueryTitle(title);
            var spellings = new[]
            {
                queryTitle,
                AudiobookSearchQueryBuilder.BuildTitleStem(queryTitle),
                TitleUtils.NormalizeTitle(title)
            };

            foreach (var spelling in spellings)
            {
                var required = AudiobookSearchQueryBuilder.Tokenize(spelling)
                    .Where(AudiobookSearchQueryBuilder.IsSignificantWord)
                    .Where(word => !EditionWords.Contains(word))
                    .ToHashSet(StringComparer.Ordinal);

                if (required.Count > 0 && !forms.Any(existing => existing.SetEquals(required)))
                {
                    forms.Add(required);
                }
            }

            return forms;
        }

        /// <summary>
        /// The surname of each usable author, plus the surname joined to the word before it so a
        /// particle name matches however it is spaced ("Le Guin" and "LeGuin").
        /// </summary>
        /// <remarks>
        /// Only the text before a comma is read, which is the surname in "Doyle, Arthur Conan" and
        /// the whole name in "Martin Luther King, Jr.". Initials are merged by
        /// <see cref="StringUtils.NormalizeAuthorName"/> first, so "H. G. Wells" and "HG Wells"
        /// both end in "wells".
        /// </remarks>
        private static HashSet<string> BuildSurnameKeys(IEnumerable<string>? authors)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var author in authors ?? [])
            {
                if (IsPlaceholderAuthor(author))
                {
                    continue;
                }

                var beforeComma = author.Split(',', 2)[0];
                var words = StringUtils.NormalizeAuthorName(beforeComma)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(word => !NameSuffixes.Contains(word))
                    .ToList();

                if (words.Count == 0 || words[^1].Length < 2)
                {
                    continue;
                }

                keys.Add(words[^1]);
                if (words.Count >= 2)
                {
                    keys.Add(words[^2] + words[^1]);
                }
            }

            return keys;
        }

        private static IEnumerable<string> WordsAndJoinedPairs(IReadOnlyList<string> tokens)
        {
            for (var index = 0; index < tokens.Count; index++)
            {
                yield return tokens[index];
                if (index + 1 < tokens.Count)
                {
                    yield return tokens[index] + tokens[index + 1];
                }
            }
        }

        private static bool IsPlaceholderAuthor(string? author)
        {
            var normalized = StringUtils.NormalizeAuthorName(author);
            return normalized.Length == 0 || PlaceholderAuthors.Contains(normalized);
        }
    }
}
