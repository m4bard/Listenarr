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

// The names below are the ones the provider actually returned. Asked six times for
// "George Makepeace Towle - translator" on six fresh instances, audnexus put a different George
// first nearly every time and Towle was never among them, which is what a fuzzy search over a
// name the provider carries no identifier for looks like.
namespace Listenarr.Tests.Features.Application.Metadata.Audnexus
{
    public class AudnexusAuthorIdentityTests
    {
        private static List<AudnexusAuthorSearchResult> Strangers() =>
            new()
            {
                new() { Asin = "B00O0C6Z26", Name = "George Bodenheimer" },
                new() { Asin = "B001K8SNEG", Name = "George Meegan" },
                new() { Asin = "B000APBJ7S", Name = "George Plimpton" }
            };

        [Fact]
        public void Select_TakesNobodyWhenNoRowNamesTheAuthor()
        {
            Assert.Null(AudnexusAuthorIdentity.Select(Strangers(), "George Makepeace Towle"));
        }

        // The control against overshoot: refusing the strangers must not amount to refusing the
        // search. It is also deliberately not the first row, so a rule that had quietly gone back
        // to taking the head of the list would answer Briscoe here.
        [Fact]
        public void Select_TakesTheRowThatNamesTheAuthorWhereverItSits()
        {
            var candidates = new List<AudnexusAuthorSearchResult>
            {
                new() { Asin = "B000APBJ7S", Name = "Constance Briscoe" },
                new() { Asin = "B000APTDDU", Name = "Constance Garnett" },
                new() { Asin = "B001K8SNEG", Name = "Constance Hall" }
            };

            Assert.Equal("B000APTDDU", AudnexusAuthorIdentity.Select(candidates, "Constance Garnett")?.Asin);
        }

        [Fact]
        public void Select_IgnoresCaseInTheName()
        {
            var candidates = new List<AudnexusAuthorSearchResult>
            {
                new() { Asin = "B000APTDDU", Name = "CONSTANCE GARNETT" }
            };

            Assert.Equal("B000APTDDU", AudnexusAuthorIdentity.Select(candidates, "constance garnett")?.Asin);
        }

        // An identifier the caller already has is a key, not a guess, so a row carrying it is the
        // same record whatever it is named. This is the rung the metadata lookup uses and the
        // catalogue does not, because the catalogue holds nothing to match on.
        [Fact]
        public void Select_TakesARowCarryingAnIdentifierTheCallerAlreadyHolds()
        {
            var candidates = Strangers();
            candidates.Add(new AudnexusAuthorSearchResult { Asin = "B000AQ6LZW", Name = "Jules Verne" });

            Assert.Equal(
                "B000AQ6LZW",
                AudnexusAuthorIdentity.Select(candidates, "J. Verne", heldAsin: "B000AQ6LZW")?.Asin);
        }

        [Fact]
        public void Select_HoldingNoIdentifierDoesNotFallBackToTheFirstRow()
        {
            Assert.Null(AudnexusAuthorIdentity.Select(Strangers(), "J. Verne", heldAsin: null));
            Assert.Null(AudnexusAuthorIdentity.Select(Strangers(), "J. Verne", heldAsin: "   "));
        }

        [Fact]
        public void Select_HoldingAnIdentifierNoRowCarriesStillTakesNobody()
        {
            Assert.Null(AudnexusAuthorIdentity.Select(Strangers(), "J. Verne", heldAsin: "B000AQ6LZW"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Select_AnUnnamedRowIsNeverAMatchForAnEmptyQuery(string? askedName)
        {
            var candidates = new List<AudnexusAuthorSearchResult>
            {
                new() { Asin = "B00O0C6Z26", Name = null },
                new() { Asin = "B001K8SNEG", Name = "   " }
            };

            Assert.Null(AudnexusAuthorIdentity.Select(candidates, askedName));
        }

        [Fact]
        public void Select_NothingToChooseFrom()
        {
            Assert.Null(AudnexusAuthorIdentity.Select(null, "Constance Garnett"));
            Assert.Null(AudnexusAuthorIdentity.Select(new List<AudnexusAuthorSearchResult>(), "Constance Garnett"));
        }
    }
}
