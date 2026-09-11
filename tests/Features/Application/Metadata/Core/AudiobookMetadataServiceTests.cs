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

namespace Listenarr.Tests.Features.Application.Metadata.Core
{
    public class AudiobookMetadataServiceTests
    {
        [Fact]
        public async Task GetMetadataAsync_UsesAudnexus_WhenAudibleMetadataReturnsNull()
        {
            // Arrange
            var mockSearch = new Mock<ISearchService>();
            using var httpClientForAudible = new System.Net.Http.HttpClient();
            var audibleMock = new Mock<AudibleService>(httpClientForAudible, Mock.Of<ILogger<AudibleService>>());
            var audnexusMock = new Mock<IAudnexusService>();
            var logger = Mock.Of<ILogger<AudiobookMetadataService>>();

            // Simulate two metadata sources: Audible (priority 1) then Audnexus (priority 2)
            var sources = new List<ApiConfiguration>
            {
                new ApiConfiguration { Name = "Audible", BaseUrl = "https://api.audible.com", Priority = 1, IsEnabled = true },
                new ApiConfiguration { Name = "Audnexus", BaseUrl = "https://api.audnex.us", Priority = 2, IsEnabled = true }
            };

            mockSearch.Setup(s => s.GetEnabledMetadataSourcesAsync()).ReturnsAsync(sources);

            // Audible-backed metadata returns null
            audibleMock.Setup(a => a.GetBookMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>())).ReturnsAsync((AudibleBookResponse?)null);

            // Audnexus returns a book with Image, Authors, Description and IsAdult set
            var audnexusResp = new AudnexusBookResponse
            {
                Asin = "BTESTASIN",
                Title = "Test Title",
                Image = "https://audnexus.covers/cover.jpg",
                Authors = new List<AudnexusAuthor> { new AudnexusAuthor { Asin = "BAUTH", Name = "Author One" } },
                Description = "Test description",
                IsAdult = true
            };
            audnexusMock.Setup(a => a.GetBookMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(audnexusResp);

            var svc = new AudiobookMetadataService(mockSearch.Object, audibleMock.Object, audnexusMock.Object, logger);

            // Act
            var res = await svc.GetMetadataAsync("BTESTASIN", "us", true);

            // Assert
            Assert.NotNull(res);
            var metadata = res!.Metadata;
            Assert.Equal("BTESTASIN", metadata.Asin);
            Assert.Equal("https://audnexus.covers/cover.jpg", metadata.ImageUrl);
            Assert.Equal("Audnexus", res.Source);

            // New assertions for mapped fields
            Assert.NotNull(metadata.Authors);
            Assert.Single(metadata.Authors);
            Assert.Equal("BAUTH", metadata.Authors[0].Asin);
            Assert.Equal("Test description", metadata.Description);
            Assert.True(metadata.Explicit);
        }

        [Fact]
        public async Task GetMetadataAsync_RaisesTheProviderFault_WhenNoSourceAnswered()
        {
            var (search, audible, audnexus) = TwoSources(out var httpClient);
            using var _ = httpClient;
            audible
                .Setup(a => a.GetBookMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .ThrowsAsync(new MetadataProviderThrottledException("slow down", TimeSpan.FromSeconds(30)));
            audnexus
                .Setup(a => a.GetBookMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync((AudnexusBookResponse?)null);

            var service = new AudiobookMetadataService(
                search.Object,
                audible.Object,
                audnexus.Object,
                Mock.Of<ILogger<AudiobookMetadataService>>());

            // Returning null here made the refresh walk read a throttled provider as a provider
            // that had never heard of the book, which is the difference between asking again
            // next cycle and stamping it as checked for the staleness window.
            var thrown = await Assert.ThrowsAsync<MetadataProviderThrottledException>(
                () => service.GetMetadataAsync("BTHROTTLED", "us", true));
            Assert.Equal(TimeSpan.FromSeconds(30), thrown.RetryAfter);
        }

        [Fact]
        public async Task GetMetadataAsync_PrefersASourceThatAnswers_OverAnEarlierFault()
        {
            var (search, audible, audnexus) = TwoSources(out var httpClient);
            using var _ = httpClient;
            audible
                .Setup(a => a.GetBookMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .ThrowsAsync(new HttpRequestException("connection refused"));
            audnexus
                .Setup(a => a.GetBookMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(new AudnexusBookResponse { Asin = "BFALLBACK", Title = "Answered By The Second Source" });

            var service = new AudiobookMetadataService(
                search.Object,
                audible.Object,
                audnexus.Object,
                Mock.Of<ILogger<AudiobookMetadataService>>());

            // The fault is remembered, not raised on the spot: a second provider that does
            // answer still wins, and only a walk that ends with nothing reports the failure.
            var result = await service.GetMetadataAsync("BFALLBACK", "us", true);
            Assert.NotNull(result);
            Assert.Equal("Audnexus", result!.Source);
        }

        [Fact]
        public async Task GetMetadataAsync_ReturnsNull_WhenEverySourceAnsweredAndTheirPayloadsWereUnusable()
        {
            var (search, audible, audnexus) = TwoSources(out var httpClient);
            using var _ = httpClient;
            audible
                .Setup(a => a.GetBookMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .ThrowsAsync(new System.Text.Json.JsonException("unexpected token at line 1"));
            audnexus
                .Setup(a => a.GetBookMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ThrowsAsync(new InvalidOperationException("the payload had no asin"));

            var service = new AudiobookMetadataService(
                search.Object,
                audible.Object,
                audnexus.Object,
                Mock.Of<ILogger<AudiobookMetadataService>>());

            // Both providers answered; what came back would not parse. That is a property of
            // this book, and it will be a property of it next cycle too. Raised as a provider
            // fault the book deferred forever: never stamped, so never off the head of the
            // null-first ordering, so first in the queue and first to fail on every run, with
            // the books behind it never reached.
            Assert.Null(await service.GetMetadataAsync("BPOISONED1", "us", true));
        }

        private sealed class CapturingLogger<T> : ILogger<T>
        {
            public List<string> Messages { get; } = [];

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                Messages.Add(formatter(state, exception));
        }

        [Fact]
        public async Task GetMetadataAsync_SanitizesTheAsin_SoItCannotForgeALogLine()
        {
            var (search, audible, audnexus) = TwoSources(out var httpClient);
            using var _ = httpClient;
            audible
                .Setup(a => a.GetBookMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .ThrowsAsync(new HttpRequestException("connection refused"));
            audnexus
                .Setup(a => a.GetBookMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ThrowsAsync(new HttpRequestException("connection refused"));

            var logger = new CapturingLogger<AudiobookMetadataService>();
            var service = new AudiobookMetadataService(search.Object, audible.Object, audnexus.Object, logger);

            // The ASIN is an unvalidated route argument. Written raw, a newline in it puts a
            // line of the caller's choosing into the log that a reader cannot tell from one this
            // service wrote. The sibling workflow already sanitizes; the walk that reports a
            // provider failure did not.
            await Assert.ThrowsAsync<HttpRequestException>(
                () => service.GetMetadataAsync("B0FORGED01\nWARN  everything is fine", "us", true));

            Assert.NotEmpty(logger.Messages);
            Assert.All(logger.Messages, message => Assert.DoesNotContain('\n', message));
            Assert.Contains(
                logger.Messages,
                message => message.Contains("B0FORGED01 WARN  everything is fine", StringComparison.Ordinal));
        }

        private static (Mock<ISearchService> Search, Mock<AudibleService> Audible, Mock<IAudnexusService> Audnexus)
            TwoSources(out HttpClient httpClient)
        {
            httpClient = new HttpClient();
            var search = new Mock<ISearchService>();
            search
                .Setup(s => s.GetEnabledMetadataSourcesAsync())
                .ReturnsAsync(new List<ApiConfiguration>
                {
                    new() { Name = "Audible", BaseUrl = "https://api.audible.com", Priority = 1, IsEnabled = true },
                    new() { Name = "Audnexus", BaseUrl = "https://api.audnex.us", Priority = 2, IsEnabled = true }
                });

            return (
                search,
                new Mock<AudibleService>(httpClient, Mock.Of<ILogger<AudibleService>>()),
                new Mock<IAudnexusService>());
        }
    }
}
