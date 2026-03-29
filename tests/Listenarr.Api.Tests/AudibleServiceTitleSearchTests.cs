using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Listenarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class AudibleServiceTitleSearchTests
    {
        [Fact]
        public async Task SearchByTitleAsync_UsesKeywordCatalogSearch_ForTitleOnlyQueries()
        {
            var sawKeywordRequest = false;
            using var httpClient = new HttpClient(new DelegatingHandlerMock((request, _) =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;
                var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? string.Empty);

                if (url.Contains("/1.0/catalog/products/?", StringComparison.OrdinalIgnoreCase) &&
                    query.Contains("keywords=Project Hail Mary", StringComparison.OrdinalIgnoreCase))
                {
                    sawKeywordRequest = true;
                    return Task.FromResult(JsonResponse("""
                        {
                          "products": [],
                          "total_results": 0
                        }
                        """));
                }

                if (url.Contains("/1.0/catalog/products/?", StringComparison.OrdinalIgnoreCase) &&
                    query.Contains("title=Project Hail Mary", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(JsonResponse("""
                        {
                          "products": [],
                          "total_results": 0
                        }
                        """));
                }

                throw new InvalidOperationException($"Unexpected URL in test: {url}");
            }));

            var sut = new AudibleService(httpClient, NullLogger<AudibleService>.Instance);

            var result = await sut.SearchByTitleAsync("Project Hail Mary", page: 1, limit: 50, region: "us", language: "english");

            Assert.NotNull(result);
            Assert.True(sawKeywordRequest);
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
