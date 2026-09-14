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

namespace Listenarr.Tests.Features.Infrastructure.DownloadClients.Sabnzbd
{
    [Trait("Name", "SabnzbdResponseMapperTests")]
    [Trait("Category", "DownloadClientAdapter")]
    [Trait("Third-Party", "Sabnzbd")]
    public class SabnzbdResponseMapperTests : BaseTests
    {
        [Fact]
        [Trait("Method", "MapQueueSlotToDownloadClientItem")]
        [Trait("Scenario", "ReadsRemoveCompletedDownloadsFromClientRootField")]
        public void MapQueueSlotToDownloadClientItem_ReadsRemoveCompletedDownloads_FromClientRootField()
        {
            using var slot = JsonDocument.Parse("{}");

            var clientWithRemoval = new DownloadClientConfigurationBuilder()
                .WithType("sabnzbd")
                .Build();
            clientWithRemoval.RemoveCompletedDownloads = "remove_and_delete";

            var clientWithoutRemoval = new DownloadClientConfigurationBuilder()
                .WithType("sabnzbd")
                .Build();
            clientWithoutRemoval.RemoveCompletedDownloads = "none";

            var withRemoval = SabnzbdResponseMapper.MapQueueSlotToDownloadClientItem(
                clientWithRemoval, slot.RootElement, configuredCategory: string.Empty, queueSpeed: 0);
            var withoutRemoval = SabnzbdResponseMapper.MapQueueSlotToDownloadClientItem(
                clientWithoutRemoval, slot.RootElement, configuredCategory: string.Empty, queueSpeed: 0);

            Assert.NotNull(withRemoval);
            Assert.NotNull(withoutRemoval);
            Assert.True(withRemoval!.DownloadClientInfo.RemoveCompletedDownloads);
            Assert.False(withoutRemoval!.DownloadClientInfo.RemoveCompletedDownloads);
        }
    }
}
