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
    /// Two deliberate limits, both of which exist because the alternative is worse:
    ///
    /// The role is never removed from a name that is kept. Audible's contributor catalogue holds
    /// entities whose own name carries the role, so <c>"Eleanor Marx-Aveling - translator"</c> is
    /// a key in its own right rather than a decoration on a clean one. Rewriting a stored name
    /// would silently move it to a different identity. Surplus entries are dropped; no name is
    /// ever edited.
    ///
    /// A book is never left with nobody. An anthology credited only to its editors would
    /// otherwise lose every credit it has, which is worse than showing an editor as an author.
    /// When every credit names a role the list is returned untouched.
    /// </remarks>
    public static class AuthorCredits
    {
        // Role words Audible actually uses, gathered from its own catalogue rather than guessed,
        // including the non-English spellings it carries for translated editions.
        private const string RoleWords =
            "translator|traducteur|traductrice|traduttore|tradutor|tradutora|traducao|tradução|" +
            "translated|translation|ubersetzer|übersetzer|editor|editeur|éditeur|edited|" +
            "foreword|afterword|postface|introduction|introductions|preface|preface|préface|" +
            "avant-propos|illustrator|adapter|adaptateur|adaptation|adapted|contributor|" +
            "compiler|annotation|annotator|prologue|essay|notes";

        // Words that may sit inside a role tail without making it something other than a role:
        // joiners, and the post-nominals Audible leaves stranded after one ("editor Jr.").
        private const string TailFiller = "by|and|or|jr\\.?|sr\\.?|ph\\.?d\\.?|m\\.?d\\.?|series";

        private const string TailBody =
            "(?:\\s*(?:" + RoleWords + "|" + TailFiller + ")\\s*)+";

        // A trailing role tail introduced by a dash. Audible is inconsistent about the spacing
        // ("Gems -introduction by" as well as "Garnett - translator") and uses both hyphen and
        // en dash, so the separator is whitespace plus a dash plus optional whitespace.
        private static readonly Regex DashTail = new(
            "\\s[-\u2013]\\s*" + TailBody + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // The same thing in parentheses, which Audible uses interchangeably and sometimes as
        // well: "A. M. Sheridan Smith(Translated by)", "Alfred Lin (Foreword By) - introduction".
        private static readonly Regex ParenthesisedTail = new(
            "\\s*\\(" + TailBody + "\\)\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// True when a credited name ends in a contributor role rather than naming an author.
        /// </summary>
        /// <remarks>
        /// Only a trailing tail counts, and only when every word in that tail is role vocabulary
        /// or a joiner. Three things it deliberately does not match:
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
