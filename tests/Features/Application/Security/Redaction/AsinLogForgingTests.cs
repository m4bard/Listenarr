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

namespace Listenarr.Tests.Features.Application.Security.Redaction
{
    /// <summary>
    /// An ASIN is a route parameter on the metadata endpoints and a request field on the search
    /// ones, so it is caller-supplied text. A value carrying a newline forges an extra line in
    /// any plain-text log sink, and a reader of that sink cannot tell the forged line from a real
    /// one. Every log statement that reports an ASIN runs it through LogRedaction.SanitizeText,
    /// which flattens newlines, tabs and carriage returns and bounds the length.
    /// </summary>
    [Trait("Name", "AsinLogForgingTests")]
    [Trait("Category", "Application")]
    public class AsinLogForgingTests : BaseTests
    {
        private const string ForgedAsin =
            "B01E633FQM\n2026-01-01 00:00:00 INFO Authentication disabled by administrator";

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
                Func<TState, Exception?, string> formatter)
            {
                Messages.Add(formatter(state, exception));
            }
        }

        private static MetadataConverters Converter(ILogger<MetadataConverters> logger) =>
            new(imageCacheService: null, logger, requestContextAccessor: null);

        [Fact]
        public void SanitizeText_FlattensAForgedLogLine()
        {
            var sanitized = LogRedaction.SanitizeText(ForgedAsin);

            Assert.DoesNotContain('\n', sanitized);
            Assert.DoesNotContain('\r', sanitized);
            Assert.Contains("B01E633FQM", sanitized);
        }

        [Fact]
        public void MetadataConverter_DoesNotLetAForgedAsinReachTheLog()
        {
            var logger = new CapturingLogger<MetadataConverters>();

            Converter(logger).ConvertAudnexusToMetadata(
                new AudnexusBookResponse { Asin = ForgedAsin, Title = "She and Allan" },
                ForgedAsin);

            Assert.NotEmpty(logger.Messages);
            Assert.All(logger.Messages, m => Assert.DoesNotContain('\n', m));
            Assert.All(logger.Messages, m => Assert.DoesNotContain('\r', m));
        }

        [Fact]
        public void MetadataConverter_StillReportsTheAsinItWasGiven()
        {
            var logger = new CapturingLogger<MetadataConverters>();

            Converter(logger).ConvertAudnexusToMetadata(
                new AudnexusBookResponse { Asin = "B00CQ5WAXW", Title = "She and Allan" },
                "B00CQ5WAXW");

            // The control for the test above: sanitising must not amount to dropping the value,
            // or a log with no ASIN in it at all would pass the forging assertion.
            Assert.Contains(logger.Messages, m => m.Contains("B00CQ5WAXW", StringComparison.Ordinal));
        }

        [Fact]
        public async Task MetadataLookup_DoesNotLetAForgedAsinReachTheLog()
        {
            var logger = new CapturingLogger<AudiobookMetadataService>();
            var searchService = new Mock<ISearchService>();
            searchService
                .Setup(s => s.GetEnabledMetadataSourcesAsync())
                .ReturnsAsync(new List<ApiConfiguration>());

            using var httpClient = new System.Net.Http.HttpClient();
            var audible = new Mock<AudibleService>(httpClient, Mock.Of<ILogger<AudibleService>>());
            var audnexus = new Mock<IAudnexusService>();

            var service = new AudiobookMetadataService(
                searchService.Object, audible.Object, audnexus.Object, logger);

            var result = await service.GetMetadataAsync(ForgedAsin);

            Assert.Null(result);
            Assert.NotEmpty(logger.Messages);
            Assert.All(logger.Messages, m => Assert.DoesNotContain('\n', m));
            Assert.All(logger.Messages, m => Assert.DoesNotContain('\r', m));
        }
    }
}
