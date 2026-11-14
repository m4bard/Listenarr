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

using Listenarr.Application.Search.Discovery;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search
{
    [Trait("Name", "MonitoredBookIdentityIndexTests")]
    [Trait("Category", "Infrastructure")]
    public class MonitoredBookIdentityIndexTests : BaseTests
    {
        private const string Title = "A Princess of Mars";
        private const string Author = "Edgar Rice Burroughs";
        private const string FirstAsin = "B008DFUGCQ";
        private const string SecondAsin = "B071YLS9YL";

        private static readonly MonitoredBookIdentity First = new(1, Title, [Author], FirstAsin);
        private static readonly MonitoredBookIdentity Second = new(2, Title, [Author], SecondAsin);

        private static SearchResult Release(string title, string? asin = null, string? artist = null) =>
            new() { Title = title, Asin = asin, Artist = artist };

        [Fact]
        public void Match_AuthorAndTitleRelease_MatchesTheMonitoredBook()
        {
            var index = new MonitoredBookIdentityIndex([First]);

            var match = index.Match(Release($"{Author} - {Title}"));

            Assert.Equal(MonitoredBookMatchOutcome.Matched, match.Outcome);
            Assert.Equal(1, match.AudiobookId);
        }

        [Fact]
        public void Match_SameTitleWithUnknownAuthorAndNoAsin_IsNoMatch()
        {
            var index = new MonitoredBookIdentityIndex([First]);

            var match = index.Match(Release(Title, artist: "Unknown Author"));

            Assert.Equal(MonitoredBookMatchOutcome.NoMatch, match.Outcome);
        }

        [Fact]
        public void Match_TwoMonitoredBooksSatisfyTheRelease_IsAmbiguous()
        {
            var index = new MonitoredBookIdentityIndex([First, Second]);

            var match = index.Match(Release($"{Author} - {Title}"));

            Assert.Equal(MonitoredBookMatchOutcome.Ambiguous, match.Outcome);
            Assert.Null(match.AudiobookId);
        }

        [Fact]
        public void Match_DifferentAuthorWithTheSameTitle_IsNoMatch()
        {
            var index = new MonitoredBookIdentityIndex([First]);

            var match = index.Match(Release($"Jules Verne - {Title}"));

            Assert.Equal(MonitoredBookMatchOutcome.NoMatch, match.Outcome);
        }

        [Fact]
        public void Match_BookNotInTheIndex_IsNoMatch()
        {
            var index = new MonitoredBookIdentityIndex([]);

            var match = index.Match(Release($"{Author} - {Title}"));

            Assert.Equal(MonitoredBookMatchOutcome.NoMatch, match.Outcome);
        }

        [Fact]
        public void Match_ReleaseCarryingOneAsin_MatchesOnlyThatBook()
        {
            var index = new MonitoredBookIdentityIndex([First, Second]);

            var match = index.Match(Release(Title, asin: FirstAsin));

            Assert.Equal(MonitoredBookMatchOutcome.Matched, match.Outcome);
            Assert.Equal(1, match.AudiobookId);
        }

        [Theory]
        [InlineData("Edgar Rice Burroughs - A Princess of Mars")]
        [InlineData("Edgar Rice Burroughs - A Princess of Mars - 2012")]
        [InlineData("Edgar Rice Burroughs - A Princess of Mars - [M4B]")]
        [InlineData("Edgar Rice Burroughs - A Princess of Mars - 2012 - M4B")]
        [InlineData("Edgar Rice Burroughs – A Princess of Mars")]
        [InlineData("Edgar Rice Burroughs - A Princess of Mars - Barsoom")]
        [InlineData("A Princess of Mars - 2012")]
        public void Match_AndTheFallbackClassifier_AgreeOnEveryReleaseLayout(string releaseTitle)
        {
            var release = Release(releaseTitle, asin: FirstAsin);
            var book = new AudiobookBuilder()
                .WithId(1).WithTitle(Title).WithAuthor(Author).WithAsin(FirstAsin).Build();

            var indexMatches = new MonitoredBookIdentityIndex([First]).Match(release).Outcome
                == MonitoredBookMatchOutcome.Matched;
            var classifierKeeps = new AutomaticSearchResultClassifier(NullLogger.Instance)
                .ConstrainFallbackResultsToTitle([release], book).Count == 1;

            Assert.Equal(classifierKeeps, indexMatches);
        }
    }
}
