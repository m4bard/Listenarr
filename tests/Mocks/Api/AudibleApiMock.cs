using Listenarr.Tests.Common;

namespace Listenarr.Tests.Mocks.Api
{
    public class AudibleApiMock : BaseApiMock
    {
        public static readonly string SINGLE_FILE_NZBGET = "101";
        public static readonly string MULTI_FILE_NZBGET = "202";

        public AudibleApiMock()
        {
            AddRoute("/1.0/screens/audible-android-author-detail/B00G0WYW92", GetScreensEmpty, HttpMethod.Get);
            AddRoute("/1.0/screens/audible-android-author-detail/B004XRR8Z6", GetScreensEmpty, HttpMethod.Get);
            AddRoute("/1.0/screens/audible-android-author-detail/B0DTNVW7SG", GetScreensEmpty, HttpMethod.Get);
            AddRoute("/author/Andy-Weir/B00G0WYW92", GetAuthorAndyWeir, HttpMethod.Get);
            AddRoute("/author/Ernest-Cline/B004XRR8Z6", GetAuthorErnestCline, HttpMethod.Get);
            AddRoute("/author/SenLinYu/B0DTNVW7SG", GetAuthorSenLinYu, HttpMethod.Get);
            AddRoute("/1.0/catalog/products/B005FRGT44", GetProductReadyPlayerOne, HttpMethod.Get);
            AddRoute("/1.0/catalog/products/0593396960", GetProductReadyPlayerTwo, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?.*keywords=Project Hail Mary", GetProductsProjectHailMary, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?.*title=Project Hail Mary", GetProductsEmpty, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?.*author=Stephen", GetProductsStephenKing, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?(?=[^?]*author=SenLinYu)(?=[^?]*products_sort_by=Relevance)", GetProductsSenLinYu, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?(?=[^?]*author=SenLinYu)(?=[^?]*products_sort_by=BestSellers)", GetProductsEmpty, HttpMethod.Get);

            AddRoute(@"/1\.0/catalog/products/\?.*author=Jane Austen", GetProductsAustenCollection, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?.*author=Charlotte Bronte", GetProductsAustenCollection, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?.*author=Sir Arthur Conan Doyle", GetProductsConanDoyle, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?.*author=Émile Zola", GetProductsEmileZola, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?.*author=Mary Shelley", GetProductsMaryShelley, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?.*author=Herman Melville", GetProductsHermanMelville, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?.*author=Bram Stoker", GetProductsBramStoker, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?.*author=Wilkie Collins", GetProductsWilkieCollins, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/products/\?.*author=Alexandre Dumas", GetProductsAlexandreDumas, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/contributors/FIXTUREAUT1\?", GetContributorConanDoyle, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/contributors/FIXTUREAUT2\?", GetContributorEmileZola, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/contributors/FIXTUREAUT3\?", GetContributorMaryShelley, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/contributors/FIXTUREAUT4\?", GetContributorHawthorne, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/contributors/FIXTUREAUT5\?", GetContributorHermanMelville, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/contributors/FIXTUREAUT6\?", GetContributorServerError, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/contributors/FIXTUREAUT7\?", GetContributorSomebodyElse, HttpMethod.Get);
            AddRoute(@"/1\.0/catalog/contributors/FIXTUREAUT9\?", GetContributorDumasFils, HttpMethod.Get);
        }

        private async Task<HttpResponseMessage> GetProductReadyPlayerOne(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "product": {
                    "asin": "B005FRGT44",
                    "title": "Ready Player One",
                    "language": "english",
                    "authors": [{ "name": "Ernest Cline", "asin": "B004XRR8Z6" }],
                    "sku": "sku1",
                    "merchandising_summary": "desc1",
                    "publisher_name": "Random House Audio",
                    "runtime_length_min": 960
                }
            }
            """);
        }

        private async Task<HttpResponseMessage> GetProductReadyPlayerTwo(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "product": {
                    "asin": "0593396960",
                    "title": "Ready Player Two",
                    "language": "english",
                    "authors": [{ "name": "Ernest Cline", "asin": "B004XRR8Z6" }],
                    "sku": "sku2",
                    "merchandising_summary": "desc2",
                    "publisher_name": "Random House Audio",
                    "runtime_length_min": 900
                }
            }
            """);
        }

        private async Task<HttpResponseMessage> GetProductsStephenKing(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "products": [
                    {
                        "asin": "B0077DEH7A",
                        "title": "The Stand",
                        "language": "english",
                        "authors": [{ "name": "Stephen King" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook",
                        "sku": "sku-stand",
                        "merchandising_summary": "desc1",
                        "publisher_name": "Random House Audio",
                        "runtime_length_min": 2867
                    },
                    {
                        "asin": "B005UR3VFO",
                        "title": "11-22-63",
                        "language": "english",
                        "authors": [{ "name": "Stephen King" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook",
                        "sku": "sku-112263",
                        "merchandising_summary": "desc2",
                        "publisher_name": "Simon & Schuster Audio",
                        "runtime_length_min": 1840
                    },
                    {
                        "asin": "B00NOJ47PU",
                        "title": "Not Stephen King",
                        "language": "english",
                        "authors": [{ "name": "Somebody Else" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook",
                        "sku": "sku-other",
                        "merchandising_summary": "desc3",
                        "publisher_name": "Elsewhere",
                        "runtime_length_min": 600
                    },
                    {
                        "asin": "B019WPM4ZM",
                        "title": "It",
                        "language": "english",
                        "authors": [{ "name": "Stephen King" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook",
                        "sku": "sku-it",
                        "merchandising_summary": "desc4",
                        "publisher_name": "Simon & Schuster Audio",
                        "runtime_length_min": 2683
                    }
                ],
                "total_results": 4
            }
            """);
        }

        private async Task<HttpResponseMessage> GetProductsSenLinYu(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
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
            """);
        }

        private async Task<HttpResponseMessage> GetProductsProjectHailMary(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "products": [
                    {
                        "asin": "BBBBBBBBBB",
                        "title": "Project Hail Mary",
                        "authors": [{ "name": "Andy Weir", "asin": "AAAAAAAAAA" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    }
                ],
                "total_results": 1
            }
            """);
        }

        // Author-identity fixtures. Every ASIN below is a fixture literal, not a real identifier.
        // FIXTUREAUT* are ids the contributor endpoint confirms; FIXTURESHR* are ids credited
        // beside more than one name and confirmed by nothing.

        private async Task<HttpResponseMessage> GetProductsAustenCollection(HttpRequestMessage request, CancellationToken ct)
        {
            // The shape from AudibleApiMock's own Stephen King fixture -- a credit with no id --
            // sitting beside a credit that has one and only contains the name being searched for.
            return MockUtils.GetCannedResponse("""
            {
                "products": [
                    {
                        "asin": "FIXTUREPRD1",
                        "title": "Pride and Prejudice",
                        "language": "english",
                        "authors": [{ "name": "Jane Austen" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    },
                    {
                        "asin": "FIXTUREPRD2",
                        "title": "Two Classic Novels",
                        "language": "english",
                        "authors": [{ "name": "Jane Austen and Charlotte Bronte", "asin": "FIXTURESHR1" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    }
                ],
                "total_results": 2
            }
            """);
        }

        private async Task<HttpResponseMessage> GetProductsConanDoyle(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "products": [
                    {
                        "asin": "FIXTUREPRD3",
                        "title": "The Hound of the Baskervilles",
                        "language": "english",
                        "authors": [{ "name": "Arthur Conan Doyle", "asin": "FIXTUREAUT1" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    }
                ],
                "total_results": 1
            }
            """);
        }

        private async Task<HttpResponseMessage> GetProductsEmileZola(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "products": [
                    {
                        "asin": "FIXTUREPRD4",
                        "title": "Germinal",
                        "language": "english",
                        "authors": [{ "name": "Emile Zola", "asin": "FIXTUREAUT2" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    }
                ],
                "total_results": 1
            }
            """);
        }

        private async Task<HttpResponseMessage> GetProductsMaryShelley(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "products": [
                    {
                        "asin": "FIXTUREPRD5",
                        "title": "Gothic Tales",
                        "language": "english",
                        "authors": [{ "name": "Mary Shelley", "asin": "FIXTURESHR2" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    },
                    {
                        "asin": "FIXTUREPRD6",
                        "title": "Frankenstein",
                        "language": "english",
                        "authors": [{ "name": "Mary Shelley", "asin": "FIXTUREAUT3" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    }
                ],
                "total_results": 2
            }
            """);
        }

        private async Task<HttpResponseMessage> GetProductsHermanMelville(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "products": [
                    {
                        "asin": "FIXTUREPRD7",
                        "title": "Sea Stories",
                        "language": "english",
                        "authors": [{ "name": "Herman Melville", "asin": "FIXTUREAUT4" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    },
                    {
                        "asin": "FIXTUREPRD8",
                        "title": "Moby Dick",
                        "language": "english",
                        "authors": [{ "name": "Herman Melville", "asin": "FIXTUREAUT5" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    }
                ],
                "total_results": 2
            }
            """);
        }

        private async Task<HttpResponseMessage> GetProductsBramStoker(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "products": [
                    {
                        "asin": "FIXTUREPRD9",
                        "title": "Dracula",
                        "language": "english",
                        "authors": [{ "name": "Bram Stoker", "asin": "FIXTUREAUT6" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    },
                    {
                        "asin": "FIXTUREPRDA",
                        "title": "Victorian Horror Omnibus",
                        "language": "english",
                        "authors": [{ "name": "Bram Stoker and Others", "asin": "FIXTURESHR3" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    }
                ],
                "total_results": 2
            }
            """);
        }

        private async Task<HttpResponseMessage> GetProductsWilkieCollins(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "products": [
                    {
                        "asin": "FIXTUREPRDB",
                        "title": "The Moonstone",
                        "language": "english",
                        "authors": [{ "name": "Wilkie Collins", "asin": "FIXTUREAUT7" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    }
                ],
                "total_results": 1
            }
            """);
        }

        private async Task<HttpResponseMessage> GetProductsAlexandreDumas(HttpRequestMessage request, CancellationToken ct)
        {
            // Two credits of equal length, so nothing but how many products each is credited on
            // separates them.
            return MockUtils.GetCannedResponse("""
            {
                "products": [
                    {
                        "asin": "FIXTUREPRDC",
                        "title": "Camille",
                        "language": "english",
                        "authors": [{ "name": "Alexandre Dumas pere", "asin": "FIXTUREAUT8" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    },
                    {
                        "asin": "FIXTUREPRDD",
                        "title": "The Count of Monte Cristo",
                        "language": "english",
                        "authors": [{ "name": "Alexandre Dumas fils", "asin": "FIXTUREAUT9" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    },
                    {
                        "asin": "FIXTUREPRDE",
                        "title": "The Three Musketeers",
                        "language": "english",
                        "authors": [{ "name": "Alexandre Dumas fils", "asin": "FIXTUREAUT9" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    },
                    {
                        "asin": "FIXTUREPRDF",
                        "title": "Twenty Years After",
                        "language": "english",
                        "authors": [{ "name": "Alexandre Dumas fils", "asin": "FIXTUREAUT9" }],
                        "content_type": "Product",
                        "content_delivery_type": "MultiPartBook"
                    }
                ],
                "total_results": 4
            }
            """);
        }

        private async Task<HttpResponseMessage> GetContributorConanDoyle(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "contributor": {
                    "contributor_id": "FIXTUREAUT1",
                    "name": "Arthur Conan Doyle",
                    "profile_image_url": "https://example.invalid/images/fixture-aut1.jpg",
                    "bio": "Fixture biography."
                }
            }
            """);
        }

        private async Task<HttpResponseMessage> GetContributorEmileZola(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "contributor": {
                    "contributor_id": "FIXTUREAUT2",
                    "name": "Emile Zola",
                    "profile_image_url": "https://example.invalid/images/fixture-aut2.jpg",
                    "bio": "Fixture biography."
                }
            }
            """);
        }

        private async Task<HttpResponseMessage> GetContributorMaryShelley(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "contributor": {
                    "contributor_id": "FIXTUREAUT3",
                    "name": "Mary Shelley",
                    "profile_image_url": "https://example.invalid/images/fixture-aut3.jpg",
                    "bio": "Fixture biography."
                }
            }
            """);
        }

        private async Task<HttpResponseMessage> GetContributorHawthorne(HttpRequestMessage request, CancellationToken ct)
        {
            // Resolves, but to somebody other than the name it was credited beside.
            return MockUtils.GetCannedResponse("""
            {
                "contributor": {
                    "contributor_id": "FIXTUREAUT4",
                    "name": "Nathaniel Hawthorne",
                    "profile_image_url": "https://example.invalid/images/fixture-aut4.jpg",
                    "bio": "Fixture biography."
                }
            }
            """);
        }

        private async Task<HttpResponseMessage> GetContributorHermanMelville(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "contributor": {
                    "contributor_id": "FIXTUREAUT5",
                    "name": "Herman Melville",
                    "profile_image_url": "https://example.invalid/images/fixture-aut5.jpg",
                    "bio": "Fixture biography."
                }
            }
            """);
        }

        private async Task<HttpResponseMessage> GetContributorSomebodyElse(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "contributor": {
                    "contributor_id": "FIXTUREAUT7",
                    "name": "Somebody Else",
                    "profile_image_url": "https://example.invalid/images/fixture-aut7.jpg",
                    "bio": "Fixture biography."
                }
            }
            """);
        }

        private async Task<HttpResponseMessage> GetContributorDumasFils(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "contributor": {
                    "contributor_id": "FIXTUREAUT9",
                    "name": "Alexandre Dumas fils",
                    "profile_image_url": "https://example.invalid/images/fixture-aut9.jpg",
                    "bio": "Fixture biography."
                }
            }
            """);
        }

        private async Task<HttpResponseMessage> GetContributorServerError(HttpRequestMessage request, CancellationToken ct)
        {
            // Reachable-but-failing, which is not the same signal as "this is not a contributor".
            await Task.CompletedTask;
            return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(string.Empty)
            };
        }

        private async Task<HttpResponseMessage> GetProductsEmpty(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "products": [],
                "total_results": 0
            }
            """);
        }

        private async Task<HttpResponseMessage> GetScreensEmpty(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            {
                "sections": [
                    {
                        "model": {
                            "rows": []
                        }
                    }
                ]
            }
            """);
        }

        private async Task<HttpResponseMessage> GetAuthorAndyWeir(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            <html><body>
            <adbl-full-width-product-tile>
                <adbl-full-bleed-image data-asin="B08G9PRS1K"
                                        data-url="/pd/Project-Hail-Mary-Audiobook/B08G9PRS1K"
                                        portrait-src="https://m.media-amazon.com/images/I/B1jkwD8awiL.png">
                </adbl-full-bleed-image>
                <h2 slot="title">Project Hail Mary</h2>
                <adbl-product-metadata slot="metadata">
                <script type="application/json">
                    {"authors":[{"name":"Andy Weir"}]}
                </script>
                </adbl-product-metadata>
                <adbl-button href="/pd/Project-Hail-Mary-Audiobook/B08G9PRS1K">View Details</adbl-button>
            </adbl-full-width-product-tile>
            </body></html>
            """, "text/html");
        }

        private async Task<HttpResponseMessage> GetAuthorErnestCline(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
            <html><body><ul class="bc-list bc-list-nostyle">
            <li class="bc-list-item productListItem" id="product-list-item-B005FRGT44" aria-label="Ready Player One">
                <a href="/pd/Ready-Player-One-Audiobook/B005FRGT44">
                <div class="adbl-asin-impression" data-asin="B005FRGT44">
                    <img src="https://m.media-amazon.com/images/I/41Eptolyo+L._SL500_.jpg" />
                </div>
                </a>
                <h2>Ready Player One</h2>
            </li>
            <li class="bc-list-item productListItem" id="product-list-item-0593396960" aria-label="Ready Player Two">
                <a href="/pd/Ready-Player-Two-Audiobook/0593396960">
                <div class="adbl-asin-impression" data-asin="0593396960">
                    <img src="https://m.media-amazon.com/images/I/51XI-UQzsAL._SL500_.jpg" />
                </div>
                </a>
                <h2>Ready Player Two</h2>
            </li>
            </ul></body></html>
            """, "text/html");
        }

        private async Task<HttpResponseMessage> GetAuthorSenLinYu(HttpRequestMessage request, CancellationToken ct)
        {
            return MockUtils.GetCannedResponse("""
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
            """, "text/html");
        }
    }
}
