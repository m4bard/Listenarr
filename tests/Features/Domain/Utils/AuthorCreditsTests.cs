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

using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Utils
{
    /// <summary>
    /// The rule is only as good as the thing that decides what a role credit is, so most of
    /// these are about the detector rather than the filtering.
    /// </summary>
    /// <remarks>
    /// A detector that matched every name and a detector that matched none would both pass a
    /// test suite that only ever checks translators get dropped, so every case here is paired
    /// with one that has to come out the other way. Every credited name quoted is one Audible
    /// actually returns, taken from a catalogue sample rather than invented, because a made-up
    /// string can be made to match anything.
    /// </remarks>
    [Trait("Name", nameof(AuthorCreditsTests))]
    [Trait("Category", "AuthorCredits")]
    public class AuthorCreditsTests : BaseTests
    {
        [Theory]
        [InlineData("Constance Garnett - translator")]
        [InlineData("George Makepeace Towle - translator")]
        [InlineData("Samuel Butler - translator")]
        [InlineData("Ralph Manheim - translated")]
        [InlineData("Marlaine Delargy - Translated by")]
        [InlineData("James L. Snyder - editor")]
        [InlineData("Geoffrey R. Stone - edited by")]
        [InlineData("Marty Ross - adapted by")]
        [InlineData("Anthony A. Barrett - introduction")]
        [InlineData("Yogi Berra - foreword")]
        [InlineData("Dr. Akiko Iwasaki - afterword")]
        [InlineData("Guy Newland - editor and translator")]
        [InlineData("Theodore C. Van Alst - editor Jr.")]
        [InlineData("Gerald R. Gems -introduction by")]
        [InlineData("A. M. Sheridan Smith(Translated by)")]
        [InlineData("Alfred Lin (Foreword By) - introduction")]
        public void IsRoleCredit_RecognisesTheNotationsAudibleActuallyUses(string credit)
        {
            Assert.True(AuthorCredits.IsRoleCredit(credit));
        }

        [Theory]
        [InlineData("Fyodor Dostoevsky")]
        [InlineData("Jules Verne")]
        [InlineData("O. Henry")]
        [InlineData("Hector Hugh Munro (Saki)")]
        [InlineData("Louis-Ferdinand Celine")]
        [InlineData("Jean-Paul Sartre")]
        [InlineData("Yang Jing - Yang Jing")]
        [InlineData("Jonathan Maberry - editor/author")]
        [InlineData("Alison Tyler - author/editor")]
        [InlineData("Martin Luther King (Jr.)")]
        [InlineData("Sammy Davis (Jr.)")]
        [InlineData("Gabor Maté (M.D.)")]
        [InlineData("Jordan B. Peterson (Ph.D.)")]
        [InlineData("Daniel G. Amen - M.D.")]
        [InlineData("Deepak Chopra - MD")]
        [InlineData("Jane Doe - Series")]
        [InlineData("Ali Smith - and")]
        [InlineData("Gedeon - Andor")]
        [InlineData("John Smith, Ph.D.")]
        [InlineData("Robert Downey Jr.")]
        [InlineData("")]
        [InlineData(null)]
        public void IsRoleCredit_LeavesEverythingElseAlone(string? credit)
        {
            Assert.False(AuthorCredits.IsRoleCredit(credit));
        }

        [Fact]
        public void IsRoleCredit_DoesNotFireOnADashThatIsNotACredit()
        {
            // The control for a detector keying on punctuation instead of vocabulary. Audible
            // credits transliterated names this way, and a hyphenated surname is ordinary.
            Assert.False(AuthorCredits.IsRoleCredit("Zhao Fanyu - Zhao Fanyu"));
            Assert.False(AuthorCredits.IsRoleCredit("Eleanor Marx-Aveling"));

            // And the same name with a role on the end must still be caught, or the test above
            // would pass just as well against a detector that never fires at all.
            Assert.True(AuthorCredits.IsRoleCredit("Eleanor Marx-Aveling - translator"));
        }

        [Fact]
        public void IsRoleCredit_DoesNotFireOnARoleWordThatIsPartOfTheName()
        {
            // A role word only counts as a credit in the trailing tail. Somebody surnamed
            // Editor is an author, and so is somebody whose book has Translator in the byline
            // position for a reason of their own.
            Assert.False(AuthorCredits.IsRoleCredit("Editor Jones"));
            Assert.False(AuthorCredits.IsRoleCredit("Translator Smith"));
            Assert.True(AuthorCredits.IsRoleCredit("Jones - editor"));
        }

        [Fact]
        public void AuthorsOnly_DropsTheContributorAndKeepsTheAuthor()
        {
            // B002V9ZF3K, Crime and Punishment, as the catalogue returns it.
            var credits = new List<string> { "Fyodor Dostoevsky", "Constance Garnett - translator" };

            Assert.Equal(new[] { "Fyodor Dostoevsky" }, AuthorCredits.AuthorsOnly(credits));
        }

        [Fact]
        public void AuthorsOnly_GivesTheSameAnswerWhenTheOrderIsReversed()
        {
            // B00EZAXAF8, The Brothers Karamazov, which credits the same pair the other way
            // round. This is the case that made the positional rule untenable: keeping index 0
            // here files Dostoevsky's novel under his translator. The two assertions have to
            // agree, and under "keep the first credit" they would not.
            var asListed = new List<string> { "Fyodor Dostoevsky", "Constance Garnett - translator" };
            var reversed = new List<string> { "Constance Garnett - translator", "Fyodor Dostoevsky" };

            Assert.Equal(AuthorCredits.AuthorsOnly(asListed), AuthorCredits.AuthorsOnly(reversed));
            Assert.Equal(new[] { "Fyodor Dostoevsky" }, AuthorCredits.AuthorsOnly(reversed));
        }

        [Fact]
        public void AuthorsOnly_LeavesASingleAuthorUntouched()
        {
            // The control against dropping things wholesale. B008DFUGCQ.
            var credits = new List<string> { "Edgar Rice Burroughs" };

            Assert.Equal(new[] { "Edgar Rice Burroughs" }, AuthorCredits.AuthorsOnly(credits));
        }

        [Fact]
        public void AuthorsOnly_KeepsBothOfAGenuineCoAuthorPair()
        {
            // B007ZEANIS. Two credits, neither naming a role, so neither is surplus. A rule
            // that kept only the first would lose one of these, which is the loss the
            // positional rule takes and this one does not.
            var credits = new List<string> { "O. Henry", "William Sydney Porter" };

            Assert.Equal(new[] { "O. Henry", "William Sydney Porter" }, AuthorCredits.AuthorsOnly(credits));
        }

        [Fact]
        public void AuthorsOnly_KeepsEveryCreditWhenTheyAllNameARole()
        {
            // 1094179574, an anthology credited only to its two editors. Dropping the role
            // credits here would leave the book with nobody, which is worse than the defect
            // being fixed, so the list comes back as it went in.
            var credits = new List<string> { "Lisa Morton - editor", "Leslie S. Klinger - editor" };

            Assert.Equal(credits, AuthorCredits.AuthorsOnly(credits));
        }

        [Fact]
        public void AuthorsOnly_KeepsTheOnlyCreditWhenItNamesARole()
        {
            // The single-credit form of the same case, which is the commoner one: a translated
            // classic credited to nobody but its translator.
            var credits = new List<string> { "Stephen Mitchell - translator" };

            Assert.Equal(credits, AuthorCredits.AuthorsOnly(credits));
        }

        [Fact]
        public void AuthorsOnly_DropsSeveralContributorsAtOnce()
        {
            // 1541437438. One author, two different kinds of contributor after it.
            var credits = new List<string>
            {
                "Tacitus",
                "J. C. Yardley - translated",
                "Anthony A. Barrett - introduction"
            };

            Assert.Equal(new[] { "Tacitus" }, AuthorCredits.AuthorsOnly(credits));
        }

        [Fact]
        public void AuthorsOnly_HandlesNothingToFilter()
        {
            Assert.Empty(AuthorCredits.AuthorsOnly(new List<string>()));
            Assert.Empty(AuthorCredits.AuthorsOnly(null));
        }

        [Fact]
        public void Primary_PicksTheAuthorWhicheverEndOfTheBylineItIsOn()
        {
            Assert.Equal(
                "Fyodor Dostoevsky",
                AuthorCredits.Primary(new List<string> { "Fyodor Dostoevsky", "Constance Garnett - translator" }));
            Assert.Equal(
                "Fyodor Dostoevsky",
                AuthorCredits.Primary(new List<string> { "Constance Garnett - translator", "Fyodor Dostoevsky" }));
        }

        [Fact]
        public void Primary_FallsBackToTheFirstCreditWhenEveryOneNamesARole()
        {
            Assert.Equal(
                "Lisa Morton - editor",
                AuthorCredits.Primary(new List<string> { "Lisa Morton - editor", "Leslie S. Klinger - editor" }));
        }

        [Fact]
        public void Primary_ReturnsNothingWhenThereIsNothingToChoose()
        {
            Assert.Null(AuthorCredits.Primary(new List<string>()));
            Assert.Null(AuthorCredits.Primary(null));
        }
    }
}
