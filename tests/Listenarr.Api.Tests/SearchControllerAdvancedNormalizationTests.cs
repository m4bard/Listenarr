using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Api.Controllers;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class SearchControllerAdvancedNormalizationTests
    {
        [Fact]
        public async Task AdvancedSearch_StripsStructuredTitlePrefix_BeforeBuildingUnifiedQuery()
        {
            var mockService = new Mock<ISearchService>();
            string? capturedQuery = null;
            mockService
                .Setup(s => s.IntelligentSearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<double>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string query, int _, int _, string _, bool _, double _, string _, string? _, CancellationToken _) =>
                {
                    capturedQuery = query;
                    return Task.FromResult(new List<MetadataSearchResult>());
                });

            var logger = Mock.Of<ILogger<SearchController>>();
            var mockAudibleService = new Mock<AudibleService>(new System.Net.Http.HttpClient(), Mock.Of<ILogger<AudibleService>>());
            var mockMetadataService = new Mock<IAudiobookMetadataService>();
            var controller = new SearchController(mockService.Object, logger, mockAudibleService.Object, mockMetadataService.Object);
            controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };

            var req = new Listenarr.Api.Models.SearchRequest
            {
                Mode = Listenarr.Api.Models.SearchMode.Advanced,
                Title = "TITLE:Project Hail Mary",
                Language = "english"
            };

            var reqJson = JsonSerializer.SerializeToElement(req);
            var actionResult = await controller.Search(reqJson);

            Assert.NotNull(actionResult);
            Assert.Equal("TITLE:Project Hail Mary", capturedQuery);
        }
    }
}
