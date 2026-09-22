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

namespace Listenarr.Tests.Features.Application.Downloads.Submission
{
    /// <summary>
    /// The selector gained a constructor dependency, and a container that registers the
    /// selector without the state holder throws only at the moment something resolves it.
    /// Nothing resolved it from the shared test container on the day this was written, so
    /// the gap would have surfaced as an unrelated failure in whichever test reached for
    /// it next.
    /// </summary>
    [Trait("Area", "DownloadClientSelection")]
    [Trait("Name", "DownloadClientSelectorResolutionTests")]
    [Trait("Category", "Application")]
    public class DownloadClientSelectorResolutionTests : BaseTests
    {
        [Fact]
        [Trait("Scenario", "SelectorResolvesFromTheSharedTestContainer")]
        public void TheSelector_ResolvesWithAllOfItsDependencies()
        {
            var selector = _provider.GetRequiredService<DownloadClientSelector>();

            Assert.NotNull(selector);
        }

        [Fact]
        [Trait("Scenario", "RotationCursorIsSharedAcrossScopes")]
        public void TheRotationCursor_IsTheSameInstanceInEveryScope()
        {
            // Two resolutions from the root provider would be the same instance even for a
            // scoped registration, so that arrangement proves nothing. Separate scopes are
            // what distinguishes the lifetimes, and separate scopes are also what the real
            // callers use: the automatic-search sweep resolves the selector from a fresh
            // scope on every cycle. A scoped or transient cursor would restart the rotation
            // each time and the second client would go back to being unreachable.
            using var firstScope = _provider.CreateScope();
            using var secondScope = _provider.CreateScope();

            var first = firstScope.ServiceProvider.GetRequiredService<DownloadClientRoundRobinState>();
            var second = secondScope.ServiceProvider.GetRequiredService<DownloadClientRoundRobinState>();

            Assert.Same(first, second);
        }
    }
}
