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
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Features.Configuration
{
    /// <summary>
    /// The settings page reads each client's failure status from here to badge the card.
    /// </summary>
    [Trait("Area", "DownloadClientStatus")]
    [Trait("Name", "DownloadClientStatusControllerTests")]
    [Trait("Category", "Api")]
    public class DownloadClientStatusControllerTests : BaseTests
    {
        [Fact]
        [Trait("Scenario", "ListsStatus")]
        public async Task Get_ReturnsEachRecordedStatus_WithTimesMarkedAsUtc()
        {
            var disabledTill = new DateTime(2026, 9, 23, 13, 0, 0, DateTimeKind.Unspecified);
            var service = new Mock<IDownloadClientStatusService>();
            service
                .Setup(s => s.GetStatusesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DownloadClientStatusSnapshot("qb-main", 3, disabledTill.AddHours(-2), disabledTill.AddHours(-1), disabledTill, true),
                    new DownloadClientStatusSnapshot("sab-main", 1, disabledTill, disabledTill, null, false)
                ]);

            var result = await new DownloadClientStatusController(service.Object).GetStatuses(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var body = Assert.IsAssignableFrom<IEnumerable<DownloadClientStatusResponse>>(ok.Value).ToList();
            Assert.Equal(2, body.Count);

            var blocked = body.Single(s => s.ClientId == "qb-main");
            Assert.True(blocked.IsBlocked);
            Assert.Equal(3, blocked.EscalationLevel);
            Assert.Equal(disabledTill, blocked.DisabledTill);

            // SQLite hands times back with no kind, and a bare timestamp is read as local time by
            // the browser. Marking them UTC puts the Z on the wire.
            Assert.Equal(DateTimeKind.Utc, blocked.DisabledTill!.Value.Kind);
            Assert.Equal(DateTimeKind.Utc, blocked.InitialFailure!.Value.Kind);

            var failing = body.Single(s => s.ClientId == "sab-main");
            Assert.False(failing.IsBlocked);
            Assert.Null(failing.DisabledTill);
        }
    }
}
