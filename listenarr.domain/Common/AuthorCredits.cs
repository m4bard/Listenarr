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
        // The vocabulary is split because the two notations are not equally trustworthy.
        //
        // An agent noun names a person, so it can only be a credit. A participle or an abstract
        // noun describes an activity, and Amazon and Audible use exactly those words in brackets
        // to describe the WORK rather than the person: "(Illustrated)", "(Annotated)",
        // "(Adapted)" sit on a large share of public-domain classics. Reading one of those as a
        // contributor credit removes the actual author, which is the failure this class exists
        // to prevent, arrived at from the other direction.
        private const string AgentRoleWords =
            "translator|traducteur|traductrice|traduttore|tradutor|tradutora|traductor|" +
            "traductora|ubersetzer|übersetzer|editor|editora|editeur|éditeur|illustrator|" +
            "adapter|adaptateur|annotator|compiler|contributor";

        // "editora" is deliberately in the list above and is the weakest member of it: in
        // Portuguese it usually means a publishing house, and Listenarr has a Publisher field of
        // its own. It is kept because a Brazilian edited volume does credit an "editora" as a
        // person, and dropped credits are cheaper to notice than missing ones. Noted so the next
        // reader knows the ambiguity was seen rather than missed.

        private const string WorkRoleWords =
            "translated|translation|traducao|tradução|traduccion|traducción|edited|adapted|" +
            "adaptado|adaptation|illustrated|annotation|introduction|introductions|introduccion|" +
            "introducción|foreword|afterword|preface|préface|prefacio|postface|avant-propos|" +
            "prologue|prologo|prólogo|essay|notes";

        // Words that may sit inside a role tail without being the role themselves: joiners, and
        // the post-nominals Audible leaves stranded after one ("editor Jr."). On their own they
        // mean nothing, which is why both tails below require an actual role word as well.
        private const string TailFiller = "by|and|or|jr\\.?|sr\\.?|ph\\.?d\\.?|m\\.?d\\.?|series";

        // Agent nouns take a plural: Audible credits "translators" and "editors" on volumes with
        // more than one. The work words are left alone, since "notes" is already the plural form
        // and the rest do not pluralise as credits.
        private const string AgentAny = "(?:" + AgentRoleWords + ")s?";

        private const string AnyRole = AgentAny + "|" + WorkRoleWords;
        private const string TailRest = "(?:\\s+(?:" + AnyRole + "|" + TailFiller + "))*";
        private const string TailLead = "(?:(?:" + TailFiller + ")\\s+)*";

        // After a dash, both halves of the vocabulary are safe. A dash tail is how Audible writes
        // a role suffix, so "Ralph Manheim - translated" has to keep matching.
        private const string DashTailBody =
            TailLead + "(?:" + AnyRole + ")" + TailRest;

        // Inside brackets, a participle or an abstract noun only counts as a credit when it is
        // followed by "by". That keeps "Smith(Translated by)" and "Ned Asta (Illustrator)" while
        // refusing "Lewis Carroll (Illustrated)" and "Miguel de Cervantes (adapted)".
        private const string ParenTailBody =
            TailLead + "(?:" + AgentAny + "|(?:" + WorkRoleWords + ")\\s+by)" + TailRest;

        // Every separator inside a tail is mandatory whitespace. That is not tidiness: the
        // previous pattern put optional whitespace on both sides of an alternation inside a
        // repetition, which let a tail be split exponentially many ways and took ninety seconds
        // on a 133 character input. There is now exactly one way to split any tail.
        //
        // NonBacktracking removes the blowup by construction rather than by careful authoring,
        // and the timeout is a backstop for that reasoning being wrong. Compiled is not requested
        // alongside it.
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

        private const RegexOptions TailOptions =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

        // Audible is inconsistent about the spacing ("Gems -introduction by" as well as
        // "Garnett - translator") and uses hyphen, en dash and em dash.
        private static readonly Regex DashTail = new(
            "\\s[-\u2013\u2014]\\s*" + DashTailBody + "$", TailOptions, MatchTimeout);

        // The padding inside the brackets lives here rather than inside the shared tail constant.
        // Putting it back in the constant is what made the pattern exponential, and while
        // NonBacktracking would now absorb that, the call-site version stays correct if anyone
        // ever removes the option.
        private static readonly Regex ParenthesisedTail = new(
            "\\s*\\(\\s*" + ParenTailBody + "\\s*\\)\\s*$", TailOptions, MatchTimeout);

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
        /// also an author of the book, so the credit stays. Note how that actually works: a
        /// slash is not a separator the tail knows about, so it simply ends the tail and the
        /// match fails. The side effect is that <c>"someone - editor/translator"</c> is kept
        /// too, which is a credit that should have gone. That is the safe direction to be
        /// wrong in and it is not worth a second separator to fix.
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
            try
            {
                return ParenthesisedTail.IsMatch(trimmed) || DashTail.IsMatch(trimmed);
            }
            catch (RegexMatchTimeoutException)
            {
                // Not unreachable, just far away: a non-backtracking match is linear in the
                // input, and linear on a long enough string still passes 100 ms. Measured, it
                // takes somewhere between one and fifteen million characters, which no author
                // name will ever be, but a mangled provider payload might. If it does fire the
                // answer has to be "not a contributor". This runs per credited name during
                // ingestion, so throwing would fail the whole import over a punctuation
                // pattern, and guessing the other way would delete somebody from their own
                // book. Keeping the credit is the recoverable mistake.
                return false;
            }
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
