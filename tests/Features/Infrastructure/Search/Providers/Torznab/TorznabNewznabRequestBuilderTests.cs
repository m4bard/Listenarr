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
using Listenarr.Infrastructure.Search.Providers.Torznab;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.Torznab;

[Trait("Name", "TorznabNewznabRequestBuilderTests")]
[Trait("Category", "TorznabNewznabSearchProvider")]
public sealed class TorznabNewznabRequestBuilderTests : BaseTests
{
    [Theory]
    [InlineData("Newznab")]
    [InlineData("Torznab")]
    public void BuildUrl_InteriorHyphen_SendsTermsSeparately(string implementation)
    {
        // Given
        var indexer = CreateIndexer(implementation);

        // When
        var url = TorznabNewznabRequestBuilder.BuildUrl(indexer, "The Thirty-Nine Steps John Buchan", category: null);

        // Then
        Assert.Contains("q=The%20Thirty%20Nine%20Steps%20John%20Buchan&", url);
    }

    [Fact]
    public void BuildUrl_HyphenAfterSpace_DoesNotSendAnExclusionTerm()
    {
        // Given
        var indexer = CreateIndexer("Newznab");

        // When
        var url = TorznabNewznabRequestBuilder.BuildUrl(indexer, "Anne Of Green Gables -Complete 8-Book Box Set L.M. Montgomery", category: null);

        // Then
        Assert.Contains("q=Anne%20Of%20Green%20Gables%20Complete%208%20Book%20Box%20Set%20L.M.%20Montgomery&", url);
        Assert.DoesNotContain("-", QueryValue(url, "q"));
    }

    [Fact]
    public void BuildUrl_SpacedDashAndEdgeHyphens_CollapseToSingleSpaces()
    {
        // Given
        var indexer = CreateIndexer("Newznab");

        // When
        var url = TorznabNewznabRequestBuilder.BuildUrl(indexer, "-The Thirty-Nine Steps - John Buchan-", category: null);

        // Then
        Assert.Equal("The Thirty Nine Steps John Buchan", Uri.UnescapeDataString(QueryValue(url, "q")));
    }

    [Fact]
    public void BuildUrl_QueryWithoutHyphen_IsUnchanged()
    {
        // Given
        var indexer = CreateIndexer("Newznab");

        // When
        var url = TorznabNewznabRequestBuilder.BuildUrl(indexer, "The Thirty Nine Steps John Buchan", category: null);

        // Then
        Assert.Equal("The%20Thirty%20Nine%20Steps%20John%20Buchan", QueryValue(url, "q"));
    }

    [Fact]
    public void BuildUrl_HyphenInApiKeyAndCategory_IsPreserved()
    {
        // Given
        var indexer = CreateIndexer("Newznab");
        indexer.ApiKey = "key-with-hyphens";

        // When
        var url = TorznabNewznabRequestBuilder.BuildUrl(indexer, "The Thirty-Nine Steps", category: "3030-3040");

        // Then
        Assert.Equal("key-with-hyphens", QueryValue(url, "apikey"));
        Assert.Equal("3030-3040", QueryValue(url, "cat"));
    }

    private static string QueryValue(string url, string name)
    {
        var query = url[(url.IndexOf('?') + 1)..];
        var pair = query.Split('&').Single(part => part.StartsWith(name + "=", StringComparison.Ordinal));
        return pair[(name.Length + 1)..];
    }

    private static Indexer CreateIndexer(string implementation) => new()
    {
        Name = "Test Indexer",
        Implementation = implementation,
        Url = "https://indexer.example.com/api"
    };
}
