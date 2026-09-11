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

namespace Listenarr.Tests.Features.Application.Metadata.Core
{
    /// <summary>
    /// What the metadata walk does when a provider does not answer, as distinct from
    /// answering that it has nothing. Its own file rather than the end of
    /// <see cref="AudiobookMetadataServiceTests"/>, which every branch that adds a case
    /// to this area appends to and then conflicts over.
    /// </summary>
    [Trait("Area", "Metadata")]
    [Trait("Name", "AudiobookMetadataServiceProviderFaultTests")]
    [Trait("Category", "Application")]
    public class AudiobookMetadataServiceProviderFaultTests : BaseTests
    {
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

            // Returning null here tells the caller a throttled provider had never heard of the
            // book. The caller has no way to know better: null is the same answer it gets when
            // every source really did answer and none of them had it.
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
            // this book, and it will be a property of it on the next attempt too. Raised as a
            // provider fault it would be an outage that never clears, and a caller that retries
            // an outage would retry this book and only this book, forever.
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
            // service wrote. The sibling workflow sanitizes; so does every line here.
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
