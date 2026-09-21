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

namespace Listenarr.Domain.Common
{
    /// <summary>
    /// Tells a book's authors apart from its other credited contributors.
    /// </summary>
    /// <remarks>
    /// Audible has no role field. A translator or an editor arrives as an ordinary author name
    /// with the role appended to it, so a book comes back credited to
    /// <c>["Fyodor Dostoevsky", "Constance Garnett - translator"]</c> and the translator is
    /// indistinguishable from an author by structure alone.
    ///
    /// The obvious answer, and the one Readarr uses, is to keep the first credit and drop the
    /// rest. That is unsafe here, because the order is a publisher's editorial choice rather
    /// than a guarantee. Two editions of the same work credit the same pair in opposite orders:
    /// <c>B002V9ZF3K</c> lists Dostoevsky then Garnett, and <c>B00EZAXAF8</c> lists Garnett then
    /// Dostoevsky. Keeping index 0 attributes the second of those to its translator.
    ///
    /// So this drops the credits that name a role and keeps the ones that do not, which gives
    /// the same answer whichever order they arrive in.
    ///
    /// A book is never left with nobody. An anthology credited only to its editors would
    /// otherwise lose every credit it has, which is worse than showing an editor as an author.
    /// When every credit names a role the list is returned untouched.
    ///
    /// Open question, deliberately not settled here: whether a detected contributor should be
    /// omitted, as this does, or kept with the role removed from the end of its name. The two
    /// share this detector and differ only in the consequence. Omitting keeps the provider's
    /// strings exactly as they arrived on the books it does act on, and cannot help a book whose
    /// every credit names a role. Stripping helps those books and makes a detector mistake cost
    /// a shortened name rather than a deleted person.
    /// </remarks>
    public static class AuthorCredits
    {
        // Role words Audible actually uses, gathered from its own catalogue rather than guessed,
        // including the non-English spellings it carries for translated editions.
        private const string RoleWords =
            "translator|traducteur|traductrice|traduttore|tradutor|tradutora|traducao|tradução|" +
            "traductor|traductora|traduccion|traducción|translated|translation|ubersetzer|" +
            "übersetzer|editor|editora|editeur|éditeur|edited|foreword|afterword|postface|" +
            "introduction|introductions|introduccion|introducción|preface|préface|prefacio|" +
            "avant-propos|illustrator|illustrated|adapter|adaptateur|adaptation|adapted|" +
            "adaptado|contributor|compiler|annotation|annotator|prologue|prologo|prólogo|" +
            "essay|notes";

        // Words that may sit inside a role tail without being the role themselves: joiners, and
        // the post-nominals Audible leaves stranded after one ("editor Jr."). On their own they
        // mean nothing, which is why the tail below requires an actual role word as well.
        private const string TailFiller = "by|and|or|jr\\.?|sr\\.?|ph\\.?d\\.?|m\\.?d\\.?|series";

        // A role tail is filler, then at least one role word, then anything of either kind.
        //
        // Two properties are deliberate and both were bought by a review. Requiring a role word
        // stops a tail of pure post-nominals matching, which had been classifying
        // "Martin Luther King (Jr.)" and "Gabor Maté (M.D.)" as contributor credits. And every
        // separator is a mandatory \s+ rather than an optional \s* on both sides of an
        // alternation inside a +, which is the shape that made this regex take ninety seconds
        // on a 133 character input: there is now exactly one way to split a given tail.
        private const string TailBody =
            "(?:(?:" + TailFiller + ")\\s+)*(?:" + RoleWords + ")(?:\\s+(?:" + RoleWords + "|" + TailFiller + "))*";

        // NonBacktracking removes the blowup by construction rather than by careful authoring,
        // and the timeout is there for the case where that reasoning is wrong. Compiled is not
        // requested alongside it.
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

        private const RegexOptions TailOptions =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

        // A trailing role tail introduced by a dash. Audible is inconsistent about the spacing
        // ("Gems -introduction by" as well as "Garnett - translator") and uses hyphen, en dash
        // and em dash, so the separator is whitespace, a dash, then optional whitespace.
        private static readonly Regex DashTail = new(
            "\\s[-\u2013\u2014]\\s*" + TailBody + "$", TailOptions, MatchTimeout);

        // The same thing in parentheses, which Audible uses interchangeably and sometimes as
        // well: "A. M. Sheridan Smith(Translated by)", "Alfred Lin (Foreword By) - introduction".
        private static readonly Regex ParenthesisedTail = new(
            "\\s*\\(" + TailBody + "\\)\\s*$", TailOptions, MatchTimeout);

        /// <summary>
        /// True when a credited name ends in a contributor role rather than naming an author.
        /// </summary>
        /// <remarks>
        /// Only a trailing tail counts, and only when it contains at least one role word and
        /// nothing but role vocabulary and joiners. Four things it deliberately does not match:
        ///
        /// A dashed tail that is not a role. Audible credits transliterated names as
        /// <c>"Yang Jing - Yang Jing"</c>, and a rule keying on the dash alone would discard a
        /// real author.
        ///
        /// A combined credit such as <c>"Jonathan Maberry - editor/author"</c>. The person is
        /// also an author of the book, so the credit stays. This is the reason the tail has to
        /// be entirely role words: "editor/author" contains one and is not one.
        ///
        /// A role word anywhere but the tail. An author surnamed Editor, or one whose name
        /// contains "Foreword", is a name and not a credit.
        ///
        /// A tail of post-nominals with no role in it. <c>"Martin Luther King (Jr.)"</c> and
        /// <c>"Gabor Maté (M.D.)"</c> are names. Those suffixes are only tolerated when they
        /// trail an actual role, as Audible leaves them in <c>"editor Jr."</c>.
        /// </remarks>
        public static bool IsRoleCredit(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var trimmed = name.Trim();
            return ParenthesisedTail.IsMatch(trimmed) || DashTail.IsMatch(trimmed);
        }

        /// <summary>
        /// The credits that name an author, with contributor credits dropped.
        /// </summary>
        /// <remarks>
        /// Names are never altered, only omitted. When every credit names a role, or there is
        /// nothing to filter, the input is returned as it stands: a book with no author at all
        /// is worse than a book credited to its editor.
        /// </remarks>
        public static IReadOnlyList<string> AuthorsOnly(IReadOnlyList<string>? credits)
        {
            if (credits == null || credits.Count == 0)
            {
                return credits ?? Array.Empty<string>();
            }

            var authors = new List<string>(credits.Count);
            foreach (var credit in credits)
            {
                if (!IsRoleCredit(credit))
                {
                    authors.Add(credit);
                }
            }

            return authors.Count == 0 ? credits : authors;
        }

        /// <summary>
        /// The one credit to use where a single author is required, such as a folder name.
        /// </summary>
        /// <remarks>
        /// The first credit that names an author, which is the first credit outright on the
        /// overwhelming majority of books and differs only where a contributor is credited
        /// ahead of the author. Returns null when there is nothing to choose from, leaving the
        /// caller's own fallback in charge.
        /// </remarks>
        public static string? Primary(IReadOnlyList<string>? credits)
        {
            var authors = AuthorsOnly(credits);
            return authors.Count > 0 ? authors[0] : null;
        }
    }
}
