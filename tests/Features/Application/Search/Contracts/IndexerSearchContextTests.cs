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

[Trait("Name", "IndexerSearchContextTests")]
[Trait("Category", "Application")]
public sealed class IndexerSearchContextTests : BaseTests
{
    [Fact]
    public void IndexerSearchContext_QueryKindDefaultsToSearch()
    {
        // When
        var context = new IndexerSearchContext(SearchOrigin.Sweep, CycleAllowance: null);

        // Then
        Assert.Equal(IndexerRequestKind.Search, context.QueryKind);
    }

    [Fact]
    public void IndexerSearchContext_CarriesFallbackQueryKind()
    {
        // When
        var context = new IndexerSearchContext(
            SearchOrigin.Sweep,
            CycleAllowance: null,
            QueryKind: IndexerRequestKind.FallbackQuery);

        // Then
        Assert.Equal(IndexerRequestKind.FallbackQuery, context.QueryKind);
    }
}
