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
    /// tracker#341 Fix 2.5. Names here are invented ("Example Press", "Jane Doe") rather than
    /// taken from a catalogue sample, since there is no public-domain organization-as-author
    /// case to cite the way AuthorCreditsTests cites real public-domain authors.
    /// </summary>
    [Trait("Name", nameof(OrganizationCreditsTests))]
    [Trait("Category", "OrganizationCredits")]
    public class OrganizationCreditsTests : BaseTests
    {
        [Theory]
        [InlineData("Example Press")]
        [InlineData("Sample Publishing")]
        [InlineData("Acme Media")]
        [InlineData("Northwind Studios")]
        [InlineData("Beispiel Verlag")]
        [InlineData("Esempio Edizioni")]
        [InlineData("Exemple Éditions")]
        [InlineData("Example Ltd")]
        [InlineData("Example LLC")]
        [InlineData("Beispiel GmbH")]
        public void HasOrganizationVocabulary_RecognisesTheCitedSuffixes(string name)
        {
            Assert.True(OrganizationCredits.HasOrganizationVocabulary(name));
        }

        [Theory]
        [InlineData("Jane Doe")]
        [InlineData("Fyodor Dostoevsky")]
        [InlineData("Someone Else")]
        [InlineData("")]
        [InlineData(null)]
        public void HasOrganizationVocabulary_LeavesOrdinaryNamesAlone(string? name)
        {
            Assert.False(OrganizationCredits.HasOrganizationVocabulary(name));
        }

        [Fact]
        public void IsOrganizationCredit_MatchesOnVocabularyAloneRegardlessOfPublisher()
        {
            // tracker#341: the vocabulary half of the combined test is safe standalone, no
            // publisher information or exception check needed.
            Assert.True(OrganizationCredits.IsOrganizationCredit(
                "Example Press", publisherName: null, hasOtherFirstPositionCredit: false));
            Assert.True(OrganizationCredits.IsOrganizationCredit(
                "Example Press", publisherName: "Unrelated Publisher Co", hasOtherFirstPositionCredit: true));
        }

        [Fact]
        public void IsOrganizationCredit_MatchesOnPublisherEqualityWhenThereIsNoFirstPositionException()
        {
            // The second half of the combined test: a name with no organization vocabulary that
            // exactly matches the book's publisher, and is not independently known as a
            // first-position author elsewhere, is treated as an organization.
            Assert.True(OrganizationCredits.IsOrganizationCredit(
                "Northwind House", publisherName: "Northwind House", hasOtherFirstPositionCredit: false));

            // Case-insensitive and trimmed, since that is how the finding describes the equality
            // test.
            Assert.True(OrganizationCredits.IsOrganizationCredit(
                " northwind house ", publisherName: "Northwind House", hasOtherFirstPositionCredit: false));
        }

        [Fact]
        public void IsOrganizationCredit_DoesNotFireOnPublisherEqualityAloneWhenTheExceptionApplies()
        {
            // The control that makes the combined test safe. tracker#341 measured four false
            // positives from publisher-equality alone: self-published authors whose imprint is
            // their own name. The exception is "does not also appear as a first-position author
            // elsewhere" -- when the caller knows that exception does NOT hold (the name has
            // been seen first-position elsewhere), publisher equality must not fire.
            Assert.False(OrganizationCredits.IsOrganizationCredit(
                "Jane Doe Books", publisherName: "Jane Doe Books", hasOtherFirstPositionCredit: true));
        }

        [Fact]
        public void IsOrganizationCredit_NeedsAnActualPublisherMatchNotJustAnyPublisher()
        {
            Assert.False(OrganizationCredits.IsOrganizationCredit(
                "Jane Doe", publisherName: "Some Other Publisher", hasOtherFirstPositionCredit: false));
            Assert.False(OrganizationCredits.IsOrganizationCredit(
                "Jane Doe", publisherName: null, hasOtherFirstPositionCredit: false));
        }

        [Fact]
        public void IsOrganizationCredit_LeavesAnOrdinaryNameAlone()
        {
            Assert.False(OrganizationCredits.IsOrganizationCredit(
                "Fyodor Dostoevsky", publisherName: "Some Publisher", hasOtherFirstPositionCredit: false));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void IsOrganizationCredit_HandlesAnEmptyOrMissingName(string? name)
        {
            Assert.False(OrganizationCredits.IsOrganizationCredit(
                name, publisherName: "Anything", hasOtherFirstPositionCredit: false));
        }
    }
}
