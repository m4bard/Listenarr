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
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Api.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class AudibleService_AuthorFallbackTests
    {
        [Fact]
        public async Task GetBooksByAuthorAsync_ParsesFullBleedAudibleTiles_WhenAuthorBooksEndpointReturnsEmpty()
        {
            var handler = new StubHttpMessageHandler(request =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;

                if (url.Contains("/1.0/screens/audible-android-author-detail/B00G0WYW92", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            @"{
                                ""sections"": [
                                  {
                                    ""model"": {
                                      ""rows"": []
                                    }
                                  }
                                ]
                              }",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                if (url.Contains("/author/Andy-Weir/B00G0WYW92", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            @"<html><body>
                                <adbl-full-width-product-tile>
                                  <adbl-full-bleed-image data-asin=""B08G9PRS1K""
                                                         data-url=""/pd/Project-Hail-Mary-Audiobook/B08G9PRS1K""
                                                         portrait-src=""https://m.media-amazon.com/images/I/B1jkwD8awiL.png"">
                                  </adbl-full-bleed-image>
                                  <h2 slot=""title"">Project Hail Mary</h2>
                                  <adbl-product-metadata slot=""metadata"">
                                    <script type=""application/json"">
                                      {""authors"":[{""name"":""Andy Weir""}]}
                                    </script>
                                  </adbl-product-metadata>
                                  <adbl-button href=""/pd/Project-Hail-Mary-Audiobook/B08G9PRS1K"">View Details</adbl-button>
                                </adbl-full-width-product-tile>
                              </body></html>",
                            Encoding.UTF8,
                            "text/html")
                    };
                }

                throw new InvalidOperationException($"Unexpected request URL: {url}");
            });

            using var client = new HttpClient(handler);
            var service = new AudibleService(client, Mock.Of<ILogger<AudibleService>>());

            var result = await service.GetBooksByAuthorAsync("Andy Weir", "B00G0WYW92");

            var response = Assert.IsType<AudibleSearchResponse>(result);
            var book = Assert.Single(response.Results ?? new List<AudibleSearchResult>());
            Assert.Equal("B08G9PRS1K", book.Asin);
            Assert.Equal("Project Hail Mary", book.Title);
            Assert.Equal("https://m.media-amazon.com/images/I/B1jkwD8awiL.png", book.ImageUrl);
            Assert.Equal("https://www.audible.com/pd/Project-Hail-Mary-Audiobook/B08G9PRS1K", book.Link);
            Assert.Contains(book.Authors ?? new List<AudibleAuthor>(), author => author.Name == "Andy Weir");
        }

        [Fact]
        public async Task GetBooksByAuthorAsync_ParsesLegacyProductListItems_AndHydratesLanguage()
        {
            var handler = new StubHttpMessageHandler(request =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;

                if (url.Contains("/1.0/screens/audible-android-author-detail/B004XRR8Z6", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            @"{
                                ""sections"": [
                                  {
                                    ""model"": {
                                      ""rows"": []
                                    }
                                  }
                                ]
                              }",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                if (url.Contains("/author/Ernest-Cline/B004XRR8Z6", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            @"<html><body><ul class=""bc-list bc-list-nostyle"">
                                <li class=""bc-list-item productListItem"" id=""product-list-item-B005FRGT44"" aria-label=""Ready Player One"">
                                  <a href=""/pd/Ready-Player-One-Audiobook/B005FRGT44"">
                                    <div class=""adbl-asin-impression"" data-asin=""B005FRGT44"">
                                      <img src=""https://m.media-amazon.com/images/I/41Eptolyo+L._SL500_.jpg"" />
                                    </div>
                                  </a>
                                  <h2>Ready Player One</h2>
                                </li>
                                <li class=""bc-list-item productListItem"" id=""product-list-item-0593396960"" aria-label=""Ready Player Two"">
                                  <a href=""/pd/Ready-Player-Two-Audiobook/0593396960"">
                                    <div class=""adbl-asin-impression"" data-asin=""0593396960"">
                                      <img src=""https://m.media-amazon.com/images/I/51XI-UQzsAL._SL500_.jpg"" />
                                    </div>
                                  </a>
                                  <h2>Ready Player Two</h2>
                                </li>
                              </ul></body></html>",
                            Encoding.UTF8,
                            "text/html")
                    };
                }

                if (url.Contains("/1.0/catalog/products/B005FRGT44", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            @"{
                                ""product"": {
                                  ""asin"": ""B005FRGT44"",
                                  ""title"": ""Ready Player One"",
                                  ""language"": ""english"",
                                  ""authors"": [{ ""name"": ""Ernest Cline"", ""asin"": ""B004XRR8Z6"" }],
                                  ""sku"": ""sku1"",
                                  ""merchandising_summary"": ""desc1"",
                                  ""publisher_name"": ""Random House Audio"",
                                  ""runtime_length_min"": 960
                                }
                              }",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                if (url.Contains("/1.0/catalog/products/0593396960", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            @"{
                                ""product"": {
                                  ""asin"": ""0593396960"",
                                  ""title"": ""Ready Player Two"",
                                  ""language"": ""english"",
                                  ""authors"": [{ ""name"": ""Ernest Cline"", ""asin"": ""B004XRR8Z6"" }],
                                  ""sku"": ""sku2"",
                                  ""merchandising_summary"": ""desc2"",
                                  ""publisher_name"": ""Random House Audio"",
                                  ""runtime_length_min"": 900
                                }
                              }",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                throw new InvalidOperationException($"Unexpected request URL: {url}");
            });

            using var client = new HttpClient(handler);
            var service = new AudibleService(client, Mock.Of<ILogger<AudibleService>>());

            var result = await service.GetBooksByAuthorAsync("Ernest Cline", "B004XRR8Z6");

            var response = Assert.IsType<AudibleSearchResponse>(result);
            Assert.Equal(2, response.TotalResults);
            Assert.Collection(
                response.Results ?? new List<AudibleSearchResult>(),
                first =>
                {
                    Assert.Equal("B005FRGT44", first.Asin);
                    Assert.Equal("Ready Player One", first.Title);
                    Assert.Equal("english", first.Language);
                    Assert.Equal("https://www.audible.com/pd/Ready-Player-One-Audiobook/B005FRGT44", first.Link);
                },
                second =>
                {
                    Assert.Equal("0593396960", second.Asin);
                    Assert.Equal("Ready Player Two", second.Title);
                    Assert.Equal("english", second.Language);
                    Assert.Equal("https://www.audible.com/pd/Ready-Player-Two-Audiobook/0593396960", second.Link);
                });
        }

        [Fact]
        public async Task GetBooksByAuthorAsync_ReturnsComprehensiveDirectAuthorSearchResults_WithoutUsingFallbacks()
        {
            var handler = new StubHttpMessageHandler(request =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;

                if (url.Contains("/1.0/catalog/products/?", StringComparison.OrdinalIgnoreCase) &&
                    url.Contains("author=Stephen", StringComparison.OrdinalIgnoreCase) &&
                    url.Contains("products_sort_by=BestSellers", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            @"{
                                ""products"": [
                                  {
                                    ""asin"": ""B0077DEH7A"",
                                    ""title"": ""The Stand"",
                                    ""language"": ""english"",
                                    ""authors"": [{ ""name"": ""Stephen King"" }],
                                    ""content_type"": ""Product"",
                                    ""content_delivery_type"": ""MultiPartBook"",
                                    ""sku"": ""sku-stand"",
                                    ""merchandising_summary"": ""desc1"",
                                    ""publisher_name"": ""Random House Audio"",
                                    ""runtime_length_min"": 2867
                                  },
                                  {
                                    ""asin"": ""B005UR3VFO"",
                                    ""title"": ""11-22-63"",
                                    ""language"": ""english"",
                                    ""authors"": [{ ""name"": ""Stephen King"" }],
                                    ""content_type"": ""Product"",
                                    ""content_delivery_type"": ""MultiPartBook"",
                                    ""sku"": ""sku-112263"",
                                    ""merchandising_summary"": ""desc2"",
                                    ""publisher_name"": ""Simon & Schuster Audio"",
                                    ""runtime_length_min"": 1840
                                  },
                                  {
                                    ""asin"": ""B00NOJ47PU"",
                                    ""title"": ""Not Stephen King"",
                                    ""language"": ""english"",
                                    ""authors"": [{ ""name"": ""Somebody Else"" }],
                                    ""content_type"": ""Product"",
                                    ""content_delivery_type"": ""MultiPartBook"",
                                    ""sku"": ""sku-other"",
                                    ""merchandising_summary"": ""desc3"",
                                    ""publisher_name"": ""Elsewhere"",
                                    ""runtime_length_min"": 600
                                  },
                                  {
                                    ""asin"": ""B019WPM4ZM"",
                                    ""title"": ""It"",
                                    ""language"": ""english"",
                                    ""authors"": [{ ""name"": ""Stephen King"" }],
                                    ""content_type"": ""Product"",
                                    ""content_delivery_type"": ""MultiPartBook"",
                                    ""sku"": ""sku-it"",
                                    ""merchandising_summary"": ""desc4"",
                                    ""publisher_name"": ""Simon & Schuster Audio"",
                                    ""runtime_length_min"": 2683
                                  }
                                ],
                                ""total_results"": 4
                              }",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                throw new InvalidOperationException($"Unexpected request URL: {url}");
            });

            using var client = new HttpClient(handler);
            var service = new AudibleService(client, Mock.Of<ILogger<AudibleService>>());

            var result = await service.GetBooksByAuthorAsync("Stephen King", "B000AQ0842", 1, 50, "us");

            var response = Assert.IsType<AudibleSearchResponse>(result);
            Assert.Equal(3, response.TotalResults);
            Assert.Collection(
                response.Results ?? new List<AudibleSearchResult>(),
                first =>
                {
                    Assert.Equal("B0077DEH7A", first.Asin);
                    Assert.Equal("The Stand", first.Title);
                },
                second =>
                {
                    Assert.Equal("B005UR3VFO", second.Asin);
                    Assert.Equal("11-22-63", second.Title);
                },
                third =>
                {
                    Assert.Equal("B019WPM4ZM", third.Asin);
                    Assert.Equal("It", third.Title);
                });
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_handler(request));
            }
        }
    }
}
