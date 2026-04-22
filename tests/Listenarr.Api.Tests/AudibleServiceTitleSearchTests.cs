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
