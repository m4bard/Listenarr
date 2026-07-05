/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Metadata.Jobs
{
    [Trait("Area", "Metadata")]
    [Trait("Name", "MetadataRescanProcessorTests")]
    public sealed class MetadataRescanProcessorTests : BaseTests
    {
        [Theory]
        [InlineData(FileSystemCaseSensitivityMode.Sensitive, false)]
        [InlineData(FileSystemCaseSensitivityMode.Insensitive, true)]
        public async Task RunCycleAsync_NonAudioFile_ClearsLegacyFilePathUsingResolvedRootSemantics(
            FileSystemCaseSensitivityMode caseSensitivityMode,
            bool shouldClearLegacyPath)
        {
            var rootPath = FileService.GetTempDirectory("metadata-rescan-root");
            await _rootFolderRepository.AddAsync(new RootFolderBuilder()
                .WithName("Metadata Root")
                .WithPath(rootPath)
                .WithCaseSensitivityMode(caseSensitivityMode)
                .WithIsDefault()
                .Build());
            var audiobookPath = Path.Join(rootPath, "CaseBook", "book.txt");
            var filePath = Path.Join(rootPath, "casebook", "book.txt");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Case Book")
                .WithFilePath(audiobookPath)
                .Build());
            var file = await _audiobookFileRepository.AddAsync(new AudiobookFileBuilder()
                .WithAudiobook(audiobook)
                .WithPath(filePath)
                .Build());

            var processor = new MetadataRescanProcessor(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<MetadataRescanProcessor>.Instance);
            await processor.RunCycleAsync(CancellationToken.None);

            using var verificationScope = _provider.CreateScope();
            var verificationAudiobooks = verificationScope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var verificationFiles = verificationScope.ServiceProvider.GetRequiredService<IAudiobookFileRepository>();
            var updated = await verificationAudiobooks.GetByIdAsync(audiobook.Id);
            var removed = await verificationFiles.GetByIdAsync(file.Id);
            Assert.Null(removed);
            if (shouldClearLegacyPath)
            {
                Assert.Null(updated?.FilePath);
                Assert.Null(updated?.FileSize);
            }
            else
            {
                Assert.Equal(audiobookPath, updated?.FilePath);
            }
        }
    }
}
