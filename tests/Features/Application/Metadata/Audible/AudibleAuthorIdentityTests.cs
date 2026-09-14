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

namespace Listenarr.Tests.Features.Application.Metadata.Audible
{
    /// <summary>
    /// Which Audible id a name lookup binds as an author's identity. The binding is durable: it
    /// selects the photo, the biography and the whole catalog on the author page, and once it is
    /// cached the lookup is never consulted again, so a wrong one is self-confirming.
    /// Fixtures live in AudibleApiMock and every ASIN in them is a fixture literal.
    /// </summary>
    [Trait("Name", "AudibleAuthorIdentityTests")]
    [Trait("Category", "AudibleService")]
    [Trait("Third-Party", "Audible")]
    public class AudibleAuthorIdentityTests : BaseTests
    {
        [Fact]
        [Trait("Method", "LookupAuthorAsync")]
        public async Task LookupAuthorAsync_ExactNameWithNoId_LosesToALooseCreditThatHasOne()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Jane Austen", "us");

            // CHARACTERIZATION: the id-bearing filter runs before anything weighs match quality,
            // so a credit that merely contains the query beats one that is the query.
            Assert.NotNull(result);
            Assert.Equal("FIXTURESHR1", result!.Asin);
        }

        [Fact]
        [Trait("Method", "LookupAuthorAsync")]
        public async Task LookupAuthorAsync_SecondNameOnTheSameCredit_ResolvesToTheSameId()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Charlotte Bronte", "us");

            // CHARACTERIZATION: paired with the test above, this is the whole reported symptom --
            // one id standing in as the identity of two different authors.
            Assert.NotNull(result);
            Assert.Equal("FIXTURESHR1", result!.Asin);
        }

        [Fact]
        [Trait("Method", "LookupAuthorAsync")]
        public async Task LookupAuthorAsync_StoredNameCarryingAnHonorific_StillResolves()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Sir Arthur Conan Doyle", "us");

            // CONTROL. The remote predicate is deliberately loose so a stored string carrying a
            // title, a credential or an honorific still finds the contributor. Tightening it is
            // the regression every other test in this file could hide.
            Assert.NotNull(result);
            Assert.Equal("FIXTUREAUT1", result!.Asin);
        }

        [Fact]
        [Trait("Method", "LookupAuthorAsync")]
        public async Task LookupAuthorAsync_StoredNameCarryingDiacritics_StillResolves()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Émile Zola", "us");

            // CONTROL against any implementation that reaches for ordinal equality.
            Assert.NotNull(result);
            Assert.Equal("FIXTUREAUT2", result!.Asin);
        }

        [Fact]
        [Trait("Method", "LookupAuthorAsync")]
        public async Task LookupAuthorAsync_WinnerTheContributorEndpointDoesNotKnow_IsReturnedAnyway()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Mary Shelley", "us");

            // CHARACTERIZATION: the contributor call is fired for enrichment only, so a null --
            // which is exactly the signal that the id is not a contributor id -- falls through
            // and the id is returned regardless. The second product's confirmable id is ignored.
            Assert.NotNull(result);
            Assert.Equal("FIXTURESHR2", result!.Asin);
        }

        [Fact]
        [Trait("Method", "LookupAuthorAsync")]
        public async Task LookupAuthorAsync_WinnerTheContributorEndpointNamesDifferently_IsReturnedAnyway()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Herman Melville", "us");

            // CHARACTERIZATION: the provider itself says this id belongs to someone else, and the
            // lookup adopts that someone else's name and id as the answer for the query.
            Assert.NotNull(result);
            Assert.Equal("FIXTUREAUT4", result!.Asin);
            Assert.Equal("Nathaniel Hawthorne", result.Name);
        }

        [Fact]
        [Trait("Method", "LookupAuthorAsync")]
        public async Task LookupAuthorAsync_ContributorEndpointFailing_DoesNotFailTheLookup()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Bram Stoker", "us");

            // CONTROL. A failing provider is not evidence that the id is wrong, and an Audible
            // outage must not stop every author resolving.
            Assert.NotNull(result);
            Assert.Equal("FIXTUREAUT6", result!.Asin);
        }

        [Fact]
        [Trait("Method", "LookupAuthorAsync")]
        public async Task LookupAuthorAsync_UnambiguousResult_IsNotGatedOnTheContributorName()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Wilkie Collins", "us");

            // CONTROL in the other direction: one candidate, one id, the name asked for. There is
            // nothing to choose between, so the round trip stays an enrichment here and the id
            // survives a contributor name that disagrees.
            Assert.NotNull(result);
            Assert.Equal("FIXTUREAUT7", result!.Asin);
        }

        [Fact]
        [Trait("Method", "LookupAuthorAsync")]
        public async Task LookupAuthorAsync_AmongEquallyCloseCredits_TakesWhicheverAudibleRankedFirst()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Alexandre Dumas", "us");

            // CHARACTERIZATION: position in Audible's relevance ordering decides, even though one
            // of the two candidates is credited on three of the four returned products.
            Assert.NotNull(result);
            Assert.Equal("FIXTUREAUT8", result!.Asin);
        }
    }
}
