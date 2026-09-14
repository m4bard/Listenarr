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
        public async Task LookupAuthorAsync_ExactNameWithNoId_BeatsALooseCreditThatHasOne()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Jane Austen", "us");

            // The exact credit wins even though it carries no id, and the answer is the name
            // without one. Asserting the name alone would pass on an implementation that returns
            // the right name beside the wrong id, so the null is asserted too.
            Assert.NotNull(result);
            Assert.Equal("Jane Austen", result!.Name);
            Assert.True(string.IsNullOrEmpty(result.Asin));
        }

        [Fact]
        [Trait("Method", "LookupAuthorAsync")]
        public async Task LookupAuthorAsync_SecondNameOnTheSameCredit_ResolvesToNoId()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Charlotte Bronte", "us");

            // Paired with the test above: one id can no longer stand in as the identity of two
            // different authors. Audible does not credit this id to a contributor at all, so it is
            // not bound to either of them.
            Assert.NotNull(result);
            Assert.True(string.IsNullOrEmpty(result!.Asin));
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
        public async Task LookupAuthorAsync_WinnerTheContributorEndpointDoesNotKnow_IsPassedOver()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Mary Shelley", "us");

            // A contributor endpoint that does not know the id is the signal that it is not a
            // contributor id. That used to fall through to returning it anyway; the next ranked
            // candidate is tried instead.
            Assert.NotNull(result);
            Assert.Equal("FIXTUREAUT3", result!.Asin);
        }

        [Fact]
        [Trait("Method", "LookupAuthorAsync")]
        public async Task LookupAuthorAsync_WinnerTheContributorEndpointNamesDifferently_IsRejected()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Herman Melville", "us");

            // Audible itself says this id belongs to somebody else. Both sides of that comparison
            // are Audible's own data for the same claimed entity, which is why it can be strict
            // here without touching the looseness between the stored string and the provider name.
            Assert.NotNull(result);
            Assert.Equal("FIXTUREAUT5", result!.Asin);
            Assert.Equal("Herman Melville", result.Name);
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
        public async Task LookupAuthorAsync_AmongEquallyCloseCredits_PrefersTheWiderCreditedOne()
        {
            var service = _provider.GetRequiredService<AudibleService>();

            var result = await service.LookupAuthorAsync("Alexandre Dumas", "us");

            // Same tier, same distance from the query, so the tie falls to the candidate credited
            // across three of the four returned products rather than to whatever Audible ranked
            // first.
            Assert.NotNull(result);
            Assert.Equal("FIXTUREAUT9", result!.Asin);
        }
    }
}
