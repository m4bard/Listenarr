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
    public class AudibleServiceAuthorFallbackTests
    {
        [Fact]
        public async Task SearchByAuthorAsync_FallsBackToAudibleAuthorPage_WhenAuthorBooksEndpointReturnsNotFound()
        {
            using var httpClient = new HttpClient(new DelegatingHandlerMock((request, _) =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;

                if (url.Contains("/1.0/catalog/products/?", StringComparison.OrdinalIgnoreCase) &&
                    url.Contains("author=SenLinYu", StringComparison.OrdinalIgnoreCase) &&
                    url.Contains("products_sort_by=Relevance", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(JsonResponse("""
                        {
                          "products": [
                            {
                              "asin": "B0DQR9D4YG",
                              "title": "Alchemised",
                              "authors": [{ "name": "SenLinYu", "asin": "B0DTNVW7SG" }],
                              "content_type": "Product",
                              "content_delivery_type": "MultiPartBook"
                            }
                          ],
                          "total_results": 1
                        }
                        """));
                }

                if (url.Contains("/1.0/catalog/products/?", StringComparison.OrdinalIgnoreCase) &&
                    url.Contains("author=SenLinYu", StringComparison.OrdinalIgnoreCase) &&
                    url.Contains("products_sort_by=BestSellers", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(JsonResponse("""
                        {
                          "products": [],
                          "total_results": 0
                        }
                        """));
                }

                if (url.StartsWith("https://api.audible.com/1.0/screens/audible-android-author-detail/B0DTNVW7SG", StringComparison.Ordinal))
                {
                    return Task.FromResult(JsonResponse("""
                        {
                          "sections": [
                            {
                              "model": {
                                "rows": []
                              }
                            }
                          ]
                        }
                        """));
                }

                if (url.StartsWith("https://www.audible.com/author/SenLinYu/B0DTNVW7SG", StringComparison.Ordinal))
                {
                    return Task.FromResult(HtmlResponse(AudibleAuthorPageHtml));
                }

                throw new InvalidOperationException($"Unexpected URL in test: {url}");
            }));

            var sut = new AudibleService(httpClient, NullLogger<AudibleService>.Instance);

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

                if (url.Contains("/1.0/catalog/products/?", StringComparison.OrdinalIgnoreCase) &&
                    url.Contains("author=SenLinYu", StringComparison.OrdinalIgnoreCase) &&
                    url.Contains("products_sort_by=Relevance", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(JsonResponse("""
                        {
                          "products": [
                            {
                              "asin": "B0DQR9D4YG",
                              "title": "Alchemised",
                              "authors": [{ "name": "SenLinYu", "asin": "B0DTNVW7SG" }],
                              "content_type": "Product",
                              "content_delivery_type": "MultiPartBook"
                            }
                          ],
                          "total_results": 1
                        }
                        """));
                }

                if (url.Contains("/1.0/catalog/products/?", StringComparison.OrdinalIgnoreCase) &&
                    url.Contains("author=SenLinYu", StringComparison.OrdinalIgnoreCase) &&
                    url.Contains("products_sort_by=BestSellers", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(JsonResponse("""
                        {
                          "products": [],
                          "total_results": 0
                        }
                        """));
                }

                if (url.StartsWith("https://api.audible.com/1.0/screens/audible-android-author-detail/B0DTNVW7SG", StringComparison.Ordinal))
                {
                    return Task.FromResult(JsonResponse("""
                        {
                          "sections": [
                            {
                              "model": {
                                "rows": []
                              }
                            }
                          ]
                        }
                        """));
                }

                if (url.StartsWith("https://www.audible.com/author/SenLinYu/B0DTNVW7SG", StringComparison.Ordinal))
                {
                    return Task.FromResult(HtmlResponse(AudibleAuthorPageHtml));
                }

                throw new InvalidOperationException($"Unexpected URL in test: {url}");
            }));

            var sut = new AudibleService(httpClient, NullLogger<AudibleService>.Instance);

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
