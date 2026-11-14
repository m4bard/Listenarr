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

namespace Listenarr.Tests.Features.Application.Search.Contracts;

[Trait("Name", "SearchContractEnumTests")]
[Trait("Category", "Application")]
public sealed class SearchContractEnumTests : BaseTests
{
    [Theory]
    [InlineData(SearchOrigin.Interactive, 0)]
    [InlineData(SearchOrigin.Sweep, 1)]
    [InlineData(SearchOrigin.Retry, 2)]
    [InlineData(SearchOrigin.Bulk, 3)]
    [InlineData(SearchOrigin.Discovery, 4)]
    public void SearchOrigin_NumericValuesAreStable(SearchOrigin origin, int expected)
    {
        // Then
        Assert.Equal(expected, (int)origin);
    }

    [Theory]
    [InlineData(IndexerRequestKind.Search, 0)]
    [InlineData(IndexerRequestKind.FallbackQuery, 1)]
    [InlineData(IndexerRequestKind.Enrichment, 2)]
    [InlineData(IndexerRequestKind.Redirect, 3)]
    [InlineData(IndexerRequestKind.FeedPoll, 4)]
    [InlineData(IndexerRequestKind.FeedPage, 5)]
    public void IndexerRequestKind_NumericValuesAreStable(IndexerRequestKind kind, int expected)
    {
        // Then
        Assert.Equal(expected, (int)kind);
    }
}
