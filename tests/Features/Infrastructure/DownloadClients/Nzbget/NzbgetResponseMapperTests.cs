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
using System.Xml.Linq;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.DownloadClients.Nzbget
{
    [Trait("Name", "NzbgetResponseMapperTests")]
    [Trait("Category", "DownloadClientAdapter")]
    [Trait("Third-Party", "Nzbget")]
    public class NzbgetResponseMapperTests : BaseTests
    {
        [Fact]
        [Trait("Method", "MapGroupToDownloadClientItem")]
        [Trait("Scenario", "ReadsRemoveCompletedDownloadsFromClientRootField")]
        public void MapGroupToDownloadClientItem_ReadsRemoveCompletedDownloads_FromClientRootField()
        {
            var emptyGroup = new XElement("struct");

            var clientWithRemoval = new DownloadClientConfigurationBuilder()
                .WithType("nzbget")
                .Build();
            clientWithRemoval.RemoveCompletedDownloads = "remove";

            var clientWithoutRemoval = new DownloadClientConfigurationBuilder()
                .WithType("nzbget")
                .Build();
            clientWithoutRemoval.RemoveCompletedDownloads = "none";

            var withRemoval = NzbgetResponseMapper.MapGroupToDownloadClientItem(clientWithRemoval, emptyGroup);
            var withoutRemoval = NzbgetResponseMapper.MapGroupToDownloadClientItem(clientWithoutRemoval, emptyGroup);

            Assert.True(withRemoval.DownloadClientInfo.RemoveCompletedDownloads);
            Assert.False(withoutRemoval.DownloadClientInfo.RemoveCompletedDownloads);
        }
    }
}
