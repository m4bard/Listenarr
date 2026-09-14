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

namespace Listenarr.Tests.Features.Api.Features.Metadata;

[Trait("Name", "MetadataCacheKeysAuthorNormalizationTests")]
[Trait("Category", "Api")]
public sealed class MetadataCacheKeysAuthorNormalizationTests : BaseTests
{
    // MetadataLookupCacheWorkflow writes AuthorCacheEntries.AuthorNameNormalized through this
    // method, and AudiobookRepository.GetCachedAuthorByNameAsync reads that column through
    // StringUtils.NormalizeAuthorName. A writer and a reader that disagree produce rows nothing
    // can find by name again.
    [Theory]
    [InlineData("J. N. Chaney")]
    [InlineData("J.N. Chaney")]
    [InlineData("Émile Zola")]
    [InlineData("P P Corcoran")]
    [InlineData("Andy  Weir")]
    public void NormalizeAuthorCacheKey_AgreesWithTheReadersNormalizer(string name)
    {
        Assert.Equal(
            StringUtils.NormalizeAuthorName(name),
            MetadataCacheKeys.NormalizeAuthorCacheKey(name));
    }

    // The control that shows the pre-fix shape genuinely had the bug. NormalizeSeriesCacheKey is
    // the untouched sibling: it is still the old shared lookup-key implementation, and it splits
    // both of these pairs. Without this assertion the theory above would also pass on an
    // implementation where nothing had ever been wrong.
    [Theory]
    [InlineData("J.N. Chaney", "J. N. Chaney")]
    [InlineData("Emile Zola", "Émile Zola")]
    public void NormalizeSeriesCacheKey_StillSplitsWhatTheAuthorKeyNowMerges(string first, string second)
    {
        Assert.NotEqual(
            MetadataCacheKeys.NormalizeSeriesCacheKey(first),
            MetadataCacheKeys.NormalizeSeriesCacheKey(second));
        Assert.Equal(
            MetadataCacheKeys.NormalizeAuthorCacheKey(first),
            MetadataCacheKeys.NormalizeAuthorCacheKey(second));
    }

    // Series titles are not people's names. The initials-merging pass would be wrong for them,
    // so the fold was deliberately narrow.
    [Fact]
    public void NormalizeSeriesCacheKey_KeepsItsOwnBehaviour()
    {
        Assert.Equal("the expanse", MetadataCacheKeys.NormalizeSeriesCacheKey("The Expanse"));
        Assert.Equal("a b c", MetadataCacheKeys.NormalizeSeriesCacheKey("A B C"));
        Assert.Equal("abc", StringUtils.NormalizeAuthorName("A B C"));
    }

    [Fact]
    public void BuildAuthorLookupCacheKey_SpellingVariants_ShareOneCacheKey()
    {
        Assert.Equal(
            MetadataCacheKeys.BuildAuthorLookupCacheKey("us", "J.N. Chaney"),
            MetadataCacheKeys.BuildAuthorLookupCacheKey("us", "J. N. Chaney"));
    }
}
