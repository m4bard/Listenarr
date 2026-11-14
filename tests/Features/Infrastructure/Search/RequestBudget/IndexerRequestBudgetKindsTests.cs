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

using Listenarr.Infrastructure.Search.RequestBudget;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestBudget;

// Which request kinds the daily limit charges. The answer is a decision, so it is written out
// here in full: changing either set in production fails this file until it is updated too.
[Trait("Name", "IndexerRequestBudgetKindsTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestBudgetKindsTests : BaseTests
{
    public static TheoryData<IndexerRequestKind> AllKinds() => new(Enum.GetValues<IndexerRequestKind>());

    [Fact]
    public void Kinds_EveryKindIsInExactlyOneSet()
    {
        // Given
        var kinds = Enum.GetValues<IndexerRequestKind>();

        // When
        var unclassified = kinds
            .Where(kind => IndexerRequestBudgetKinds.Charged.Contains(kind) == IndexerRequestBudgetKinds.Uncharged.Contains(kind))
            .ToList();

        // Then
        Assert.Empty(unclassified);
        Assert.Equal(kinds.Length, IndexerRequestBudgetKinds.Charged.Count + IndexerRequestBudgetKinds.Uncharged.Count);
    }

    [Fact]
    public void Kinds_OnlyGrabsGoUncharged()
    {
        // Given / When / Then
        Assert.Equal([IndexerRequestKind.DownloadFetch], IndexerRequestBudgetKinds.Uncharged.Order());
        Assert.Equal(
            [
                IndexerRequestKind.Search,
                IndexerRequestKind.FallbackQuery,
                IndexerRequestKind.Enrichment,
                IndexerRequestKind.Redirect,
                IndexerRequestKind.ConnectionTest
            ],
            IndexerRequestBudgetKinds.Charged.Order());
    }

    [Fact]
    public void Kinds_AKindInNeitherSet_IsCharged()
    {
        // Given
        var unknown = (IndexerRequestKind)int.MaxValue;

        // When / Then
        Assert.True(IndexerRequestBudgetKinds.IsCharged(unknown));
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task Charge_KindPolicy(IndexerRequestKind kind)
    {
        // Given: one indexer with room and one whose sweep share is 0.
        await using var database = await IndexerRequestBudgetTestDatabase.CreateAsync();
        var withRoom = await database.AddIndexerAsync(5);
        var exhausted = await database.AddIndexerAsync(1);
        var charged = IndexerRequestBudgetKinds.Charged.Contains(kind);

        // When
        var admitted = await database.ReserveAsync(withRoom, SearchOrigin.Sweep, kind);
        var againstNoRoom = await database.ReserveAsync(exhausted, SearchOrigin.Sweep, kind);

        // Then
        Assert.Null(admitted);
        Assert.Equal(charged ? 1 : 0, await database.SpentAsync(withRoom));
        Assert.Equal(charged ? RequestDeferralReason.AllowanceExhausted : null, againstNoRoom?.Reason);
        Assert.Equal(0, await database.SpentAsync(exhausted));
    }
}
