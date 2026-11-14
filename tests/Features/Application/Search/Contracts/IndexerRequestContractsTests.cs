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

[Trait("Name", "IndexerRequestContractsTests")]
[Trait("Category", "Application")]
public sealed class IndexerRequestContractsTests : BaseTests
{
    private sealed class DefaultReservation : IIndexerRequestReservation
    {
        public Task<RequestDeferral?> TryReserveAsync(
            IndexerRequestContext context,
            CancellationToken cancellationToken) => Task.FromResult<RequestDeferral?>(null);
    }

    [Fact]
    public void DeferredException_KeepsItsDeferral()
    {
        // Given
        var deferral = new RequestDeferral(
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            RequestDeferralReason.RateLimited);

        // When
        var exception = new IndexerRequestDeferredException(deferral);

        // Then
        Assert.Equal(deferral, exception.Deferral);
    }

    [Fact]
    public void EnumValues_AreDistinct()
    {
        // Given / When
        var reasons = Enum.GetValues<RequestDeferralReason>();
        var kinds = Enum.GetValues<IndexerRequestKind>();

        // Then
        Assert.Equal(reasons.Length, reasons.Distinct().Count());
        Assert.Equal(kinds.Length, kinds.Distinct().Count());
        Assert.Contains(RequestDeferralReason.ReservationUnavailable, reasons);
        Assert.Contains(IndexerRequestKind.DownloadFetch, kinds);
    }

    [Fact]
    public async Task PeekAsync_DefaultImplementation_AdmitsEverything()
    {
        // Given
        IIndexerRequestReservation reservation = new DefaultReservation();

        // When
        var deferral = await reservation.PeekAsync(1, default, CancellationToken.None);

        // Then
        Assert.Null(deferral);
    }

    [Theory]
    [InlineData(RequestDeferralReason.RateLimited, "indexer in cooldown until")]
    [InlineData(RequestDeferralReason.FailureLadder, "indexer in cooldown until")]
    [InlineData(RequestDeferralReason.CycleRequestBound, "request limit for this cycle reached")]
    [InlineData(RequestDeferralReason.ReservationUnavailable, "request could not be checked")]
    [InlineData(RequestDeferralReason.RequestStateUnavailable, "indexer request state could not be loaded")]
    public void Describe_UsesWordingThatFitsTheReason(RequestDeferralReason reason, string expectedStart)
    {
        var untilUtc = new DateTime(2026, 3, 2, 15, 0, 0, DateTimeKind.Utc);

        var text = new RequestDeferral(untilUtc, reason).Describe();

        Assert.StartsWith(expectedStart, text, StringComparison.Ordinal);
        Assert.Contains(untilUtc.ToString("O"), text, StringComparison.Ordinal);
        Assert.Contains($"({reason})", text, StringComparison.Ordinal);
    }
}
