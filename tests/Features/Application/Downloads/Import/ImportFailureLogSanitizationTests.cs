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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;

namespace Listenarr.Tests.Features.Application.Downloads.Import
{
    /// <summary>
    /// Both of the failure-warning log calls in DownloadImportService used to interpolate the
    /// absolute source path straight into the message template. That put the path in the
    /// rendered line unredacted, and put it outside field boundaries so a log shipper could not
    /// filter or group by it either. Both now pass the path as a structured argument through
    /// LogRedaction.SanitizeFilePath instead (issue #975). These tests capture the fully
    /// rendered line, the way a real log sink would receive it, and check the leak is gone from
    /// the rendered text, not merely from the source of the log call.
    /// </summary>
    [Trait("Name", "ImportFailureLogSanitizationTests")]
    [Trait("Category", "DownloadProcessingJob")]
    public class ImportFailureLogSanitizationTests : BaseTests
    {
        private MetadataServiceMock metadataServiceMock = new();

        public override async Task InitializeAsync()
        {
            _services.AddSingleton<IMetadataService>(metadataServiceMock);
            Init();
            await AddAuthorizedRootAsync(FileService.GetTempPath());
        }

        private sealed class CapturingLogger<T> : ILogger<T>
        {
            public List<string> Messages { get; } = [];

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Messages.Add(formatter(state, exception));
            }
        }

        /// <summary>
        /// Delegates every call to a real capability except for one chosen path, which throws
        /// instead. Lets a single audio file import genuinely succeed (through the real
        /// filesystem checks) while a specific companion file fails with a path-bearing
        /// exception, without having to fake the whole publication pipeline.
        /// </summary>
        private sealed class SelectiveThrowingCapability(
            IFilePublicationSourceCapability inner,
            string throwForPath,
            Exception exceptionToThrow) : IFilePublicationSourceCapability
        {
            public Task<FilePublicationSourceCapabilityResult> CheckAsync(
                string sourcePath,
                CancellationToken cancellationToken = default) =>
                string.Equals(sourcePath, throwForPath, StringComparison.Ordinal)
                    ? throw exceptionToThrow
                    : inner.CheckAsync(sourcePath, cancellationToken);
        }

        [Fact]
        public async Task AudioFileImportException_LogsSanitizedFilePathAsStructuredArgument_NotInterpolated()
        {
            const string leakCanary = "audio-log-leak-canary";
            var capturingLogger = new CapturingLogger<DownloadImportService>();
            var fileService = new Mock<IAudiobookFileService>(MockBehavior.Strict);

            var outputDirectory = FileService.GetTempDirectory("audio-log-output");
            var sourceDirectory = FileService.GetTempDirectory(leakCanary);
            var sourceFile = await FileService.GetFileAsync(sourceDirectory, "source.mp3", "audio");
            // Shaped like a real .NET DirectoryNotFoundException: it quotes the full path.
            var thrownException = new DirectoryNotFoundException(
                $"Could not find a part of the path '{sourceFile}'.");
            fileService
                .Setup(service => service.CheckAudiobookFileOwnershipAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(thrownException);

            Init(builder => builder
                .WithSingleton<IAudiobookFileService>(fileService.Object)
                .WithSingleton<ILogger<DownloadImportService>>(capturingLogger));
            await AddAuthorizedRootAsync(FileService.GetTempPath());

            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Audio Log Leak")
                .WithBasePath(outputDirectory)
                .Build());
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(outputDirectory)
                .WithCopyFileOnCompleted()
                .WithoutMetadataProcessing()
                .WithFolderNamingPattern("")
                .WithFileNamingPattern("{Title}")
                .WithMultiFileNamingPattern("{Title}")
                .Build());

            var result = Assert.Single(await _provider
                .GetRequiredService<IDownloadImportService>()
                .ImportDownloadFilesAsync(audiobook, [sourceFile]));

            Assert.False(result.Success);
            Assert.Equal(ImportFailureClass.DestinationUnavailable, result.FailureClass);

            var logLine = Assert.Single(capturingLogger.Messages, m =>
                m.Contains("Failed processing file in directory import", StringComparison.Ordinal));
            Assert.Contains(Path.GetFileName(sourceFile), logLine, StringComparison.Ordinal);
            Assert.DoesNotContain(leakCanary, logLine, StringComparison.Ordinal);
            Assert.DoesNotContain(sourceDirectory, logLine, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CompanionFileImportException_LogsSanitizedFilePathAsStructuredArgument_NotInterpolated()
        {
            const string leakCanary = "companion-log-leak-canary";
            var capturingLogger = new CapturingLogger<DownloadImportService>();
            var realCapability = _provider.GetRequiredService<IFilePublicationSourceCapability>();

            var outputDirectory = FileService.GetTempDirectory("companion-log-output");
            var sourceDirectory = FileService.GetTempDirectory(leakCanary);
            var audioFile = await FileService.GetFileAsync(sourceDirectory, "book.mp3", "audio");
            var companionFile = await FileService.GetFileAsync(sourceDirectory, "book.nfo", "notes");
            // Shaped like a real .NET IOException: it quotes the full path.
            var thrownException = new IOException(
                $"Could not find a part of the path '{companionFile}'.");

            Init(builder => builder
                .WithSingleton<IFilePublicationSourceCapability>(
                    new SelectiveThrowingCapability(realCapability, companionFile, thrownException))
                .WithSingleton<ILogger<DownloadImportService>>(capturingLogger));
            await AddAuthorizedRootAsync(FileService.GetTempPath());

            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Companion Log Leak")
                .WithBasePath(outputDirectory)
                .Build());
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(outputDirectory)
                .WithMoveFileOnCompleted()
                .WithoutMetadataProcessing()
                .WithFolderNamingPattern("")
                .WithFileNamingPattern("{Title}")
                .WithMultiFileNamingPattern("{Title}")
                .Build());

            var results = await _provider
                .GetRequiredService<IDownloadImportService>()
                .ImportDownloadFilesAsync(audiobook, [audioFile, companionFile]);

            var companionResult = Assert.Single(results, r => r.SourcePath == companionFile);
            Assert.False(companionResult.Success);

            var logLine = Assert.Single(capturingLogger.Messages, m =>
                m.Contains("Failed companion-file import", StringComparison.Ordinal));
            Assert.Contains(Path.GetFileName(companionFile), logLine, StringComparison.Ordinal);
            Assert.DoesNotContain(leakCanary, logLine, StringComparison.Ordinal);
            Assert.DoesNotContain(sourceDirectory, logLine, StringComparison.Ordinal);
        }
    }
}
