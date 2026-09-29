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

using System.Text.RegularExpressions;
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
    /// The rules, in order. The release title must carry every significant word of one spelling
    /// of the book's title, as whole words, ignoring case, accents, punctuation, edition and format
    /// words, and volume markers such as "Book" or "Vol". The spellings are the stored title, the
    /// part before a subtitle, the title with a trailing ", Book N" or ", Volume N" removed, and,
    /// only when the author is corroborated, the part after a "Series: " prefix. The book's author
    /// is corroborated when their surname appears in the release title or author field, and then
    /// the release is accepted. Otherwise a release whose author field names somebody else is
    /// rejected; the field only counts as naming somebody when it holds words that are not the
    /// book's own title, series or a number, because the Torznab parser fills it with whatever
    /// precedes the first " - " of the title. A release that names nobody is accepted when the
    /// matched title is at least
    /// <see cref="AudiobookSearchQueryBuilder.MinimumSignificantWordsToIssueAlone"/> significant
    /// words long, the same bar the query ladder uses for sending a title with no author, or when
    /// one " - " segment of the release title, brackets and noise removed, says exactly the book's
    /// title and nothing more. A book with no title or no author is judged on what it does have.
    /// </para>
    /// <para>
    /// That last rule is what separates the incident from an ordinary short-titled release. "Emma
    /// McChesney and Co" contains "Emma" and says more; "Emma [Unabridged]" says "Emma" and nothing
    /// else. It cannot separate two different books that share an exact short title when neither
    /// the author nor anything else on the release tells them apart, and with no author on the
    /// release that is not decidable from the name at all. It also accepts "Emma - Somebody Else",
    /// since the words after the dash are as likely a narrator as an author.
    /// </para>
    /// <para>
    /// Known narrowness, each a missed automatic grab rather than a wrong one unless stated:
    /// a number spelled one way in the record and another in the release ("1984" and "Nineteen
    /// Eighty-Four", "Twenty Thousand" and "20000"); British and American spellings ("Colour" and
    /// "Color"); an author field carrying a transliteration, a pen name or a translator instead of
    /// the author; and a release whose narrator comes before the dash with no author anywhere.
    /// Two wrong grabs it does not stop: a same-author book whose title contains the requested one
    /// (a sequel such as "Dune Messiah" for "Dune"), and a study guide or summary of the book that
    /// carries its title and author.
    /// </para>
    /// </remarks>
    public static class RequestedBookReleaseFilter
    {
        /// <summary>
        /// Words that describe the recording or its packaging rather than the work, so neither
        /// side is required to carry them.
        /// </summary>
        private static readonly IReadOnlySet<string> NoiseWords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "unabridged", "abridged", "audiobook", "edition", "dramatized", "dramatised",
                "mp3", "m4a", "m4b", "flac", "aac", "ogg", "opus", "kbps"
            };

        /// <summary>
        /// Words that introduce a volume number. The number that follows them is identity; the
        /// word itself is spelled too many ways ("Volume", "Vol.", "Book", "Bk") to require.
        /// </summary>
        private static readonly IReadOnlySet<string> VolumeMarkers =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "book", "bk", "volume", "vol", "part", "pt"
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

        /// <summary>A trailing ", Book 1", " Volume 2", " (Part 3)" and the like.</summary>
        private static readonly Regex VolumeTail = new(
            @"^(?<stem>.*?\S)[\s,:;\-\(\[]*\b(?:book|bk|volume|vol|part|pt)\.?\s*(?<number>\d+)\s*[\)\]]?\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>The separators the Torznab parser and most release names use between fields.</summary>
        private static readonly string[] SegmentSeparators = [" - ", " – ", " — "];

        private static readonly Regex DelimitedSpan = new(
            @"\[[^\]]*\]|\([^\)]*\)|\{[^\}]*\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private enum TitleFormKind
        {
            /// <summary>The whole title; also the only kind a bare release segment is compared with.</summary>
            Whole,

            /// <summary>Less than the whole title, still enough without the author when long enough.</summary>
            Shortened,

            /// <summary>The part after a "Series: " prefix; only with the author corroborated.</summary>
            AfterPrefix
        }

        private sealed record TitleForm(IReadOnlySet<string> Words, TitleFormKind Kind, string? VolumeNumber);

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

            var releaseTitleTokens = Words(release.Title);
            var releaseTitleWords = new HashSet<string>(releaseTitleTokens, StringComparer.Ordinal);

            var matchedForms = titleForms
                .Where(form => form.Words.All(releaseTitleWords.Contains)
                    && !NamesADifferentVolume(releaseTitleTokens, form.VolumeNumber))
                .ToList();

            if (matchedForms.Count == 0)
            {
                return RequestedBookMatch.TitleMismatch;
            }

            var surnames = BuildSurnameKeys(audiobook.Authors);
            var releaseAuthorTokens = IsPlaceholderAuthor(release.Artist)
                ? new List<string>()
                : Words(release.Artist);

            var corroborated = surnames.Overlaps(WordsAndJoinedPairs(releaseTitleTokens))
                || surnames.Overlaps(WordsAndJoinedPairs(releaseAuthorTokens));
            if (corroborated)
            {
                return RequestedBookMatch.Accepted;
            }

            // "Heir to the Empire" is the whole of "Star Wars: Heir to the Empire" to anyone who
            // knows the book, and a common enough title to anyone who does not. Only the author
            // can say which, so a release that matched nothing else does not get past here.
            var matchedWithoutAuthor = matchedForms.Where(form => form.Kind != TitleFormKind.AfterPrefix).ToList();
            if (matchedWithoutAuthor.Count == 0)
            {
                return surnames.Count == 0
                    ? RequestedBookMatch.TitleMismatch
                    : RequestedBookMatch.AuthorNotCorroborated;
            }

            if (surnames.Count == 0)
            {
                return RequestedBookMatch.Accepted;
            }

            if (NamesAnotherAuthor(releaseAuthorTokens, audiobook))
            {
                return RequestedBookMatch.AuthorMismatch;
            }

            var matchedWordCount = matchedWithoutAuthor.Max(form => form.Words.Count);
            if (matchedWordCount >= AudiobookSearchQueryBuilder.MinimumSignificantWordsToIssueAlone)
            {
                return RequestedBookMatch.Accepted;
            }

            return HasSegmentSayingExactlyTheTitle(release.Title, matchedWithoutAuthor)
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
            RequestedBookMatch.AuthorNotCorroborated => "title alone does not identify the book and the author is not on the release",
            _ => verdict.ToString()
        };

        /// <summary>
        /// The spellings of the book's title a release may carry, as sets of required words.
        /// Spellings with no significant words are left out.
        /// </summary>
        private static List<TitleForm> BuildTitleForms(string? title)
        {
            var forms = new List<TitleForm>();
            if (string.IsNullOrWhiteSpace(title))
            {
                return forms;
            }

            var queryTitle = AudiobookSearchQueryBuilder.BuildQueryTitle(title);
            Add(queryTitle, TitleFormKind.Whole, null);
            Add(TitleUtils.NormalizeTitle(title), TitleFormKind.Whole, null);
            Add(AudiobookSearchQueryBuilder.BuildTitleStem(queryTitle), TitleFormKind.Shortened, null);

            // "..., Book 1" is the series position, which a release often leaves off. The number is
            // kept aside so a release that names a different volume is still not this one.
            var volumeTail = VolumeTail.Match(queryTitle);
            if (volumeTail.Success)
            {
                Add(volumeTail.Groups["stem"].Value, TitleFormKind.Whole, NormalizeNumber(volumeTail.Groups["number"].Value));
            }

            var colon = queryTitle.IndexOf(':');
            if (colon > 0 && colon < queryTitle.Length - 1)
            {
                Add(queryTitle[(colon + 1)..], TitleFormKind.AfterPrefix, null);
            }

            return forms;

            void Add(string spelling, TitleFormKind kind, string? volumeNumber)
            {
                var required = RequiredWords(spelling);
                if (required.Count > 0 && !forms.Any(existing => existing.Kind == kind && existing.Words.SetEquals(required)))
                {
                    forms.Add(new TitleForm(required, kind, volumeNumber));
                }
            }
        }

        private static HashSet<string> RequiredWords(string? text)
        {
            return Words(text)
                .Where(AudiobookSearchQueryBuilder.IsSignificantWord)
                .Where(word => !NoiseWords.Contains(word) && !VolumeMarkers.Contains(word))
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>
        /// Tokens as the query builder produces them, with numbers written without leading zeros
        /// so "01" and "1" are the same volume.
        /// </summary>
        private static List<string> Words(string? text)
        {
            return AudiobookSearchQueryBuilder.Tokenize(text).Select(NormalizeNumber).ToList();
        }

        private static string NormalizeNumber(string token)
        {
            if (token.Length < 2 || !IsNumber(token))
            {
                return token;
            }

            var trimmed = token.TrimStart('0');
            return trimmed.Length > 0 ? trimmed : "0";
        }

        private static bool IsNumber(string token) => token.Length > 0 && token.All(char.IsAsciiDigit);

        /// <summary>
        /// Whether the release says "Vol 2" (or "Book 2", ...) for a book whose title says 1.
        /// </summary>
        private static bool NamesADifferentVolume(List<string> releaseTokens, string? volumeNumber)
        {
            if (volumeNumber is null)
            {
                return false;
            }

            for (var index = 0; index + 1 < releaseTokens.Count; index++)
            {
                if (VolumeMarkers.Contains(releaseTokens[index])
                    && IsNumber(releaseTokens[index + 1])
                    && !string.Equals(releaseTokens[index + 1], volumeNumber, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the release's author field holds anything besides the book's own title words,
        /// series words and numbers. The Torznab parser fills that field with whatever precedes the
        /// first " - " of the title, so "Barsoom 01 - A Princess of Mars" arrives with "Barsoom 01"
        /// as its author, and that must not read as a different author. "Mark Twain" does.
        /// </summary>
        private static bool NamesAnotherAuthor(List<string> releaseAuthorTokens, Audiobook audiobook)
        {
            var ownWords = new HashSet<string>(Words(audiobook.Title), StringComparer.Ordinal);
            ownWords.UnionWith(Words(audiobook.Series));

            return releaseAuthorTokens
                .Where(AudiobookSearchQueryBuilder.IsSignificantWord)
                .Where(word => !NoiseWords.Contains(word) && !IsNumber(word))
                .Any(word => !ownWords.Contains(word));
        }

        /// <summary>
        /// Whether one " - " segment of the release title, with bracketed spans, noise words and
        /// stray numbers removed, is exactly the book's whole title: every word of it and nothing
        /// else. "Kim [Unabridged] [M4B]" and "Barsoom 01 - A Princess of Mars" are; "Edna Ferber -
        /// Emma McChesney and Co" is not a match for "Emma", because no segment stops at "Emma".
        /// </summary>
        private static bool HasSegmentSayingExactlyTheTitle(string? releaseTitle, List<TitleForm> matchedForms)
        {
            if (string.IsNullOrWhiteSpace(releaseTitle))
            {
                return false;
            }

            // Only spellings the release already matched, so a volume the release contradicts
            // cannot come back in through here.
            var wholeForms = matchedForms.Where(form => form.Kind == TitleFormKind.Whole).ToList();
            foreach (var segment in releaseTitle.Split(SegmentSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var segmentWords = RequiredWords(DelimitedSpan.Replace(segment, " "));
                foreach (var form in wholeForms)
                {
                    // A year or a track number beside the title is not another work.
                    var comparable = segmentWords
                        .Where(word => !IsNumber(word) || form.Words.Contains(word))
                        .ToHashSet(StringComparer.Ordinal);

                    if (comparable.SetEquals(form.Words))
                    {
                        return true;
                    }
                }
            }

            return false;
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
