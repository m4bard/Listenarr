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
using Listenarr.Api.Controllers;
using Listenarr.Application.Interfaces;
using Listenarr.Domain.Models;
using Listenarr.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Controllers
{
    [Trait("Area", "LibraryApi")]
    [Trait("Name", "LibraryController_LibraryListSlimPayloadTests")]
    [Trait("Category", "LibraryController")]
    public class LibraryController_LibraryListSlimPayloadTests : BaseTests
    {
        [Fact]
        [Trait("Method", "GetAll")]
        [Trait("Scenario", "ReturnsSlimPayload_WithServerComputedStatus")]
        public async Task GetAll_ReturnsSlimPayload_WithServerComputedStatus()
        {
            // Given
            var book = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Slim Book")
                .WithAuthor("Author One")
                .WithGenre("Fantasy")
                .WithGenre("Adventure")
                .WithMonitored()
                .WithDescription("Detail-only field")
                .WithSubtitle("Detail Subtitle")
                .WithBasePath(FileUtils.GetAbsolutePath("library", "Slim Book"))
                .WithFilePath(FileUtils.GetAbsolutePath("library", "Slim Book", "book.m4b"))
                .WithFileSize(12345)
                .WithOpenLibraryId("OL123")
                .WithAuthorAsin("AUTHORASIN1")
                .Build());

            await _audiobookFileRepository.AddAsync(new AudiobookFileBuilder()
                .WithAudiobook(book)
                .WithPath(book.FilePath!)
                .WithSize(book.FileSize ?? 0)
                .WithFormat("m4b")
                .Build());

            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobookId(book.Id)
                .WithTitle(book.Title ?? string.Empty)
                .WithArtist("Author One")
                .WithStatus(DownloadStatus.Downloading)
                .Build());

            var controller = new LibraryController(
                _audiobookRepository,
                new Mock<IImageCacheService>().Object,
                new Mock<ILogger<LibraryController>>().Object,
                _provider.GetRequiredService<IServiceScopeFactory>(),
                _historyRepository,
                _audiobookFileRepository,
                _qualityProfileRepository,
                _downloadRepository,
                _rootFolderRepository,
                new Mock<IFileNamingService>().Object,
                applicationPathService: _provider.GetRequiredService<IApplicationPathService>(),
                libraryListService: _provider.GetRequiredService<ILibraryListService>());

            // When
            var actionResult = await controller.GetAll();

            // Then
            var ok = Assert.IsType<OkObjectResult>(actionResult);

            var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var doc = JsonDocument.Parse(json);
            var item = doc.RootElement
                .EnumerateArray()
                .Single(element => element.GetProperty("id").GetInt32() == book.Id);

            Assert.Equal("downloading", item.GetProperty("status").GetString());
            Assert.False(item.GetProperty("wanted").GetBoolean());
            Assert.True(item.TryGetProperty("genres", out var genres));
            Assert.Equal(2, genres.GetArrayLength());
            Assert.Contains(genres.EnumerateArray().Select(g => g.GetString()), value => value == "Fantasy");
            Assert.True(item.TryGetProperty("openLibraryId", out var openLibraryId));
            Assert.Equal("OL123", openLibraryId.GetString());
            Assert.Equal(book.BasePath, item.GetProperty("basePath").GetString());
            Assert.Equal(book.FilePath, item.GetProperty("filePath").GetString());
            Assert.Equal(book.FileSize, item.GetProperty("fileSize").GetInt64());
            Assert.Equal(1, item.GetProperty("fileCount").GetInt32());

            Assert.False(item.TryGetProperty("files", out _));
            Assert.False(item.TryGetProperty("description", out _));
            Assert.False(item.TryGetProperty("subtitle", out _));
        }
    }
}
