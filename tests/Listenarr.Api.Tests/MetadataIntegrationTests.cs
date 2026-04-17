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
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Moq;
using Listenarr.Domain.Models;
using Listenarr.Api.Services;
using Listenarr.Api.Services.Metadata;

namespace Listenarr.Api.Tests
{
    public class MetadataIntegrationTests
    {
        [Fact]
        public async Task EnsureAudiobookFileAsync_PersistsMetadataFromMetadataService()
        {
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var db = new ListenArrDbContext(options);
            var book = new Audiobook { Title = "IntegrationTest" };
            db.Audiobooks.Add(book);
            await db.SaveChangesAsync();

            var testFile = Path.Join(Path.GetTempPath(), $"meta-int-{Guid.NewGuid()}.m4b");
            await File.WriteAllTextAsync(testFile, "dummy");

            var metadataMock = new Mock<IMetadataService>();
            metadataMock.Setup(m => m.ExtractFileMetadataAsync(It.IsAny<string>()))
                .ReturnsAsync(new AudioMetadata { Duration = TimeSpan.FromSeconds(3210), Format = "m4b", Bitrate = 64000, SampleRate = 32000, Channels = 1 });

            var services = new ServiceCollection();
            services.AddSingleton<IMetadataService>(metadataMock.Object);
            services.AddSingleton(db);
            services.AddSingleton<IAudiobookFileRepository>(_ => new EfAudiobookFileRepository(db));
            services.AddSingleton<IAudiobookRepository>(_ => new AudiobookRepository(db));
            services.AddSingleton<IHistoryRepository>(_ => new EfHistoryRepository(db));
            services.AddSingleton<MetadataExtractionLimiter>();
            services.AddMemoryCache();

            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<AudioFileService>>();
            var svc = new AudioFileService(scopeFactory, loggerMock.Object, provider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), provider.GetRequiredService<MetadataExtractionLimiter>());

            var created = await svc.EnsureAudiobookFileAsync(book.Id, testFile, "test");
            Assert.True(created);

            var file = await db.AudiobookFiles.FirstOrDefaultAsync(f => f.AudiobookId == book.Id && f.Path == testFile);
            Assert.NotNull(file);
            Assert.Equal(3210, (int)file.DurationSeconds!.Value);
            Assert.Equal("m4b", file.Format);
            Assert.Equal(64000, file.Bitrate);
            Assert.Equal(32000, file.SampleRate);
            Assert.Equal(1, file.Channels);
        }
    }
}

