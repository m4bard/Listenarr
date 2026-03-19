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
    public class AudimetaService_AuthorFallbackTests
    {
        [Fact]
        public async Task GetBooksByAuthorAsync_ParsesFullBleedAudibleTiles_WhenAuthorBooksEndpointReturnsEmpty()
        {
            var handler = new StubHttpMessageHandler(request =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;

                if (url.Contains("/author/books/B00G0WYW92", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"results\":[],\"totalResults\":0}",
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
            var service = new AudimetaService(client, Mock.Of<ILogger<AudimetaService>>());

            var result = await service.GetBooksByAuthorAsync("Andy Weir", "B00G0WYW92");

            var response = Assert.IsType<AudimetaSearchResponse>(result);
            var book = Assert.Single(response.Results ?? new List<AudimetaSearchResult>());
            Assert.Equal("B08G9PRS1K", book.Asin);
            Assert.Equal("Project Hail Mary", book.Title);
            Assert.Equal("https://m.media-amazon.com/images/I/B1jkwD8awiL.png", book.ImageUrl);
            Assert.Equal("https://www.audible.com/pd/Project-Hail-Mary-Audiobook/B08G9PRS1K", book.Link);
            Assert.Contains(book.Authors ?? new List<AudimetaAuthor>(), author => author.Name == "Andy Weir");
        }

        [Fact]
        public async Task GetBooksByAuthorAsync_ParsesLegacyProductListItems_AndHydratesLanguage()
        {
            var handler = new StubHttpMessageHandler(request =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;

                if (url.Contains("/author/books/B004XRR8Z6", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
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

                if (url.Contains("/book/B005FRGT44", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            @"{
                                ""asin"": ""B005FRGT44"",
                                ""title"": ""Ready Player One"",
                                ""language"": ""english"",
                                ""authors"": [{ ""name"": ""Ernest Cline"" }]
                              }",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                if (url.Contains("/book/0593396960", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            @"{
                                ""asin"": ""0593396960"",
                                ""title"": ""Ready Player Two"",
                                ""language"": ""english"",
                                ""authors"": [{ ""name"": ""Ernest Cline"" }]
                              }",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                throw new InvalidOperationException($"Unexpected request URL: {url}");
            });

            using var client = new HttpClient(handler);
            var service = new AudimetaService(client, Mock.Of<ILogger<AudimetaService>>());

            var result = await service.GetBooksByAuthorAsync("Ernest Cline", "B004XRR8Z6");

            var response = Assert.IsType<AudimetaSearchResponse>(result);
            Assert.Equal(2, response.TotalResults);
            Assert.Collection(
                response.Results ?? new List<AudimetaSearchResult>(),
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
