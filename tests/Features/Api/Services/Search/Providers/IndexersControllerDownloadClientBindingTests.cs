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
using System.Net;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Services.Search.Providers
{
    /// <summary>
    /// The indexer's download client binding has to survive the settings form's save and load,
    /// and a save naming a client that does not exist is refused, as Readarr's
    /// DownloadClientExistsValidator refuses it.
    /// </summary>
    [Trait("Area", "DownloadClientSelection")]
    [Trait("Name", "IndexersControllerDownloadClientBindingTests")]
    [Trait("Category", "Api")]
    public class IndexersControllerDownloadClientBindingTests : BaseTests
    {
        private sealed class OkHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }

        private IndexersController CreateController() =>
            MockUtils.CreateIndexersController(_provider, new OkHandler());

        private async Task<DownloadClientConfiguration> AddClientAsync(string id) =>
            await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfiguration
            {
                Id = id,
                Name = id,
                Type = "qbittorrent",
                Host = "localhost",
                Port = 8080,
                IsEnabled = true
            });

        private static Indexer Tracker(string? downloadClientId) =>
            new()
            {
                Name = "Private Tracker",
                Type = "Torrent",
                Implementation = "Torznab",
                Url = "https://tracker.example.test",
                ApiKey = "key",
                IsEnabled = true,
                Priority = 25,
                AdditionalSettings = string.Empty,
                DownloadClientId = downloadClientId
            };

        [Fact]
        [Trait("Scenario", "BindingIsSavedAndReturned")]
        public async Task Update_PersistsTheBinding_AndReturnsIt()
        {
            await AddClientAsync("qb-seedbox");
            var controller = CreateController();
            var created = await _indexerRepository.AddAsync(Tracker(null));

            var result = await controller.Update(created.Id, Tracker("qb-seedbox"));

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("qb-seedbox", Assert.IsType<Indexer>(ok.Value).DownloadClientId);
            var persisted = await _indexerRepository.GetByIdAsync(created.Id);
            Assert.Equal("qb-seedbox", persisted!.DownloadClientId);
        }

        [Fact]
        [Trait("Scenario", "BindingIsSavedAndReturned")]
        public async Task Update_WithNoBinding_ClearsAStoredOne()
        {
            // Control for the test above: "Any" in the form sends no client, and that has to
            // clear the stored binding rather than leave it in place.
            await AddClientAsync("qb-seedbox");
            var controller = CreateController();
            var created = await _indexerRepository.AddAsync(Tracker("qb-seedbox"));

            await controller.Update(created.Id, Tracker(null));

            var persisted = await _indexerRepository.GetByIdAsync(created.Id);
            Assert.Null(persisted!.DownloadClientId);
        }

        [Fact]
        [Trait("Scenario", "BlankBindingMeansAny")]
        public async Task Create_WithABlankBinding_StoresNoBinding()
        {
            var controller = CreateController();

            var result = await controller.Create(Tracker("  "));

            var created = Assert.IsType<CreatedAtActionResult>(result);
            var persisted = await _indexerRepository.GetByIdAsync(Assert.IsType<Indexer>(created.Value).Id);
            Assert.Null(persisted!.DownloadClientId);
        }

        [Fact]
        [Trait("Scenario", "UnknownClientIsRefused")]
        public async Task Create_NamingAClientThatDoesNotExist_IsRefused()
        {
            var controller = CreateController();

            var result = await controller.Create(Tracker("qb-nowhere"));

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Empty(await _indexerRepository.GetAllAsync());
        }

        [Fact]
        [Trait("Scenario", "UnknownClientIsRefused")]
        public async Task Update_NamingAClientThatDoesNotExist_IsRefused_AndKeepsTheOldBinding()
        {
            await AddClientAsync("qb-seedbox");
            var controller = CreateController();
            var created = await _indexerRepository.AddAsync(Tracker("qb-seedbox"));

            var result = await controller.Update(created.Id, Tracker("qb-nowhere"));

            Assert.IsType<BadRequestObjectResult>(result);
            var persisted = await _indexerRepository.GetByIdAsync(created.Id);
            Assert.Equal("qb-seedbox", persisted!.DownloadClientId);
        }
    }
}
