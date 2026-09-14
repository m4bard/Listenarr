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
using System.Text.Json;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.DownloadClients.Transmission
{
    [Trait("Name", "TransmissionResponseMapperTests")]
    [Trait("Category", "DownloadClientAdapter")]
    [Trait("Third-Party", "Transmission")]
    public class TransmissionResponseMapperTests : BaseTests
    {
        private static readonly (bool SeedRatioLimited, double SeedRatioLimit, bool IdleSeedingLimitEnabled, int IdleSeedingLimit) NoSessionLimits = (false, 0, false, 0);

        [Fact]
        [Trait("Method", "MapDownloadClientItem")]
        [Trait("Scenario", "ReadsRemoveCompletedDownloadsFromClientRootField")]
        public void MapDownloadClientItem_ReadsRemoveCompletedDownloads_FromClientRootField()
        {
            using var torrent = JsonDocument.Parse("{}");

            var clientWithRemoval = new DownloadClientConfigurationBuilder()
                .WithType("transmission")
                .Build();
            clientWithRemoval.RemoveCompletedDownloads = "remove";

            var clientWithoutRemoval = new DownloadClientConfigurationBuilder()
                .WithType("transmission")
                .Build();
            clientWithoutRemoval.RemoveCompletedDownloads = "none";

            var withRemoval = TransmissionResponseMapper.MapDownloadClientItem(clientWithRemoval, torrent.RootElement, NoSessionLimits);
            var withoutRemoval = TransmissionResponseMapper.MapDownloadClientItem(clientWithoutRemoval, torrent.RootElement, NoSessionLimits);

            Assert.True(withRemoval.DownloadClientInfo.RemoveCompletedDownloads);
            Assert.False(withoutRemoval.DownloadClientInfo.RemoveCompletedDownloads);
        }
    }
}
