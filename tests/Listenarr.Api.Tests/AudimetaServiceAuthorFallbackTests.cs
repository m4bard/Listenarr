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
    public class AudimetaServiceAuthorFallbackTests
    {
        [Fact]
        public async Task SearchByAuthorAsync_FallsBackToAudibleAuthorPage_WhenAuthorBooksEndpointReturnsNotFound()
        {
            using var httpClient = new HttpClient(new DelegatingHandlerMock((request, _) =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;

                if (url.StartsWith("https://audimeta.de/author?cache=true&region=us&name=SenLinYu", StringComparison.Ordinal))
                {
                    return Task.FromResult(JsonResponse("[{\"asin\":\"B0DTNVW7SG\",\"name\":\"SenLinYu\",\"region\":\"us\"}]"));
                }

                if (url.StartsWith("https://audimeta.de/author/books/B0DTNVW7SG", StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                if (url.StartsWith("https://www.audible.com/author/SenLinYu/B0DTNVW7SG", StringComparison.Ordinal))
                {
                    return Task.FromResult(HtmlResponse(AudibleAuthorPageHtml));
                }

                throw new InvalidOperationException($"Unexpected URL in test: {url}");
            }));

            var sut = new AudimetaService(httpClient, NullLogger<AudimetaService>.Instance);

            var result = await sut.SearchByAuthorAsync("SenLinYu", page: 1, limit: 50, region: "us");

            Assert.NotNull(result);
            Assert.NotNull(result!.Results);
            var book = Assert.Single(result.Results!);
            Assert.Equal("B0DQR9D4YG", book.Asin);
            Assert.Equal("Alchemised", book.Title);
            Assert.Equal("https://m.media-amazon.com/images/I/51IrMtF6fzL._SL500_.jpg", book.ImageUrl);
            Assert.Equal("https://www.audible.com/pd/Alchemised-Audiobook/B0DQR9D4YG", book.Link);
            Assert.NotNull(book.Authors);
            Assert.Equal("SenLinYu", Assert.Single(book.Authors!).Name);
            Assert.Equal(1, result.TotalResults);
        }

        [Fact]
        public async Task SearchByTitleAndAuthorPagedAsync_FiltersFallbackAuthorPageResults_ByTitle()
        {
            using var httpClient = new HttpClient(new DelegatingHandlerMock((request, _) =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;

                if (url.StartsWith("https://audimeta.de/author?cache=true&region=us&name=SenLinYu", StringComparison.Ordinal))
                {
                    return Task.FromResult(JsonResponse("[{\"asin\":\"B0DTNVW7SG\",\"name\":\"SenLinYu\",\"region\":\"us\"}]"));
                }

                if (url.StartsWith("https://audimeta.de/author/books/B0DTNVW7SG", StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                if (url.StartsWith("https://www.audible.com/author/SenLinYu/B0DTNVW7SG", StringComparison.Ordinal))
                {
                    return Task.FromResult(HtmlResponse(AudibleAuthorPageHtml));
                }

                throw new InvalidOperationException($"Unexpected URL in test: {url}");
            }));

            var sut = new AudimetaService(httpClient, NullLogger<AudimetaService>.Instance);

            var result = await sut.SearchByTitleAndAuthorPagedAsync("Alchemised", "SenLinYu", page: 1, limit: 50, region: "us");

            Assert.NotNull(result);
            Assert.NotNull(result!.Results);
            var book = Assert.Single(result.Results!);
            Assert.Equal("B0DQR9D4YG", book.Asin);
            Assert.Equal("Alchemised", book.Title);
            Assert.NotNull(book.Authors);
            Assert.Equal("SenLinYu", Assert.Single(book.Authors!).Name);
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage HtmlResponse(string html)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
        }

        private const string AudibleAuthorPageHtml = """
            <html>
              <body>
                <adbl-full-width-product-tile>
                  <adbl-product-image data-asin="B0DQR9D4YG" data-url="/pd/Alchemised-Audiobook/B0DQR9D4YG" slot="image">
                    <img src="https://m.media-amazon.com/images/I/51IrMtF6fzL._SL500_.jpg" alt="" />
                  </adbl-product-image>
                  <h2 slot="title">Alchemised</h2>
                  <adbl-product-metadata slot="metadata">
                    <script type="application/json">
                      {"authors":[{"name":"SenLinYu"}]}
                    </script>
                  </adbl-product-metadata>
                </adbl-full-width-product-tile>
              </body>
            </html>
            """;
    }
}
