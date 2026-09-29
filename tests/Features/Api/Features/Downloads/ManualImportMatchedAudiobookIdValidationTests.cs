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
using Listenarr.Api.Dtos.ManualImport;
using Listenarr.Application.Common.Exceptions;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Features.Downloads
{
    /// <summary>
    /// Covers upstream #958: a manual-import item whose matchedAudiobookId is missing
    /// (binds to the int default of 0) or otherwise not positive used to reach
    /// <see cref="IFileRegistrationRecoveryService.ReconcileAudiobookWithReceiptsAsync"/>
    /// unvalidated, where it throws ArgumentOutOfRangeException and is turned into an
    /// unhandled-looking 500 by Start's generic catch. The fix rejects the request with
    /// 400 at the same boundary Path and Items are already validated at, before the
    /// filesystem-mutation gate or the audiobook-lock/reconcile pipeline are ever reached.
    /// </summary>
    public sealed class ManualImportMatchedAudiobookIdValidationTests : IDisposable
    {
        private readonly List<string> _tempDirectories = [];

        public void Dispose()
        {
            foreach (var directory in _tempDirectories)
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private string CreateTempDirectory()
        {
            var directory = Path.Join(
                Path.GetTempPath(),
                "listenarr-manual-import-matched-id-validation",
                Guid.NewGuid().ToString());
            Directory.CreateDirectory(directory);
            _tempDirectories.Add(directory);
            return directory;
        }

        /// <summary>
        /// Builds a controller with every dependency loosely mocked, except the ones a
        /// request that clears the new matchedAudiobookId check would actually reach
        /// before finishing (filesystem gate, root folders, config, coordinators, and the
        /// recovery service, which callers configure per test).
        /// </summary>
        private static ManualImportController BuildController(
            ILibraryFilesystemMutationGate filesystemMutationGate,
            IFileRegistrationRecoveryService fileRegistrationRecoveryService,
            IRootFolderService? rootFolderService = null,
            IConfigurationService? configService = null,
            IMoveQueueService? moveQueueService = null)
        {
            var rootFolderMock = new Mock<IRootFolderService>();
            rootFolderMock.Setup(service => service.GetAllAsync())
                .ReturnsAsync([]);

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(service => service.GetApplicationSettingsAsync())
                .ReturnsAsync(new ApplicationSettings());

            var moveQueueMock = new Mock<IMoveQueueService>();
            moveQueueMock.Setup(service => service.EnsureFilesystemMutationAllowedAsync(
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            return new ManualImportController(
                Mock.Of<Microsoft.Extensions.Logging.ILogger<ManualImportController>>(),
                Mock.Of<IAudiobookRepository>(),
                Mock.Of<IMetadataService>(),
                Mock.Of<IFileNamingService>(),
                configService ?? configMock.Object,
                Mock.Of<IScanQueueService>(),
                Mock.Of<IAudiobookScanService>(),
                Mock.Of<IScanPathAuthorizationService>(),
                rootFolderService ?? rootFolderMock.Object,
                Mock.Of<IFileMover>(),
                Mock.Of<IFilePublicationSourceCapability>(),
                Mock.Of<IAudiobookFileService>(),
                new LocalFileSystem(),
                new FileSystemSemanticsResolver(),
                new FilesystemMutationCoordinator(),
                new AudiobookOperationCoordinator(),
                fileRegistrationRecoveryService,
                moveQueueService ?? moveQueueMock.Object,
                Mock.Of<ILibraryDirectoryOwnershipStore>(),
                filesystemMutationGate);
        }

        [Fact]
        public async Task Start_ItemMatchedAudiobookIdOmitted_ReturnsBadRequestWithoutReconciling()
        {
            var sourceDirectory = CreateTempDirectory();
            var recovery = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
            // Configured to replicate FileRegistrationRecoveryService's real behavior for
            // a non-positive id (throws ArgumentOutOfRangeException("audiobookId")), so
            // that before the Start-level fix exists, this test fails for the real
            // reason: the id reaches reconcile, reconcile throws exactly as production
            // does, and Start's generic catch turns that into a 500 instead of the 400
            // this test expects. After the fix, reconcile is never reached at all.
            recovery.Setup(service => service.ReconcileAudiobookWithReceiptsAsync(
                    0,
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentOutOfRangeException("audiobookId"));
            var controller = BuildController(
                TestLibraryFilesystemReadiness.Ready(),
                recovery.Object);
            var request = new ManualImportRequestDto
            {
                Path = sourceDirectory,
                Mode = "interactive",
                Action = FileAction.Copy,
                Items =
                [
                    new ManualImportItemDto
                    {
                        FullPath = Path.Join(sourceDirectory, "chapter.mp3")
                        // MatchedAudiobookId intentionally left unset: binds to 0, the
                        // same shape a client omitting the field over the wire produces.
                    }
                ]
            };

            var action = await controller.Start(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(action.Result);
            var payload = System.Text.Json.JsonSerializer.Serialize(badRequest.Value);
            Assert.Contains("matchedAudiobookId", payload, StringComparison.Ordinal);
            recovery.Verify(
                service => service.ReconcileAudiobookWithReceiptsAsync(
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Start_ItemMatchedAudiobookIdNegative_ReturnsBadRequestWithoutReconciling()
        {
            var sourceDirectory = CreateTempDirectory();
            var recovery = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
            recovery.Setup(service => service.ReconcileAudiobookWithReceiptsAsync(
                    -3,
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentOutOfRangeException("audiobookId"));
            var controller = BuildController(
                TestLibraryFilesystemReadiness.Ready(),
                recovery.Object);
            var request = new ManualImportRequestDto
            {
                Path = sourceDirectory,
                Mode = "interactive",
                Action = FileAction.Copy,
                Items =
                [
                    new ManualImportItemDto
                    {
                        FullPath = Path.Join(sourceDirectory, "chapter.mp3"),
                        MatchedAudiobookId = -3
                    }
                ]
            };

            var action = await controller.Start(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(action.Result);
            var payload = System.Text.Json.JsonSerializer.Serialize(badRequest.Value);
            Assert.Contains("matchedAudiobookId", payload, StringComparison.Ordinal);
            recovery.Verify(
                service => service.ReconcileAudiobookWithReceiptsAsync(
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Start_OneItemMissingMatchedAudiobookIdAmongValidSiblings_RejectsWholeBatchWithoutReconcilingAny()
        {
            // Path and Items are validated as a whole request at the top of Start, not
            // per item (a missing Path or an empty Items list rejects everything, not
            // just the offending entry). A missing matchedAudiobookId is the same class
            // of defect: the request never had a valid shape, so the whole batch is
            // rejected the same way, and the valid sibling item is never touched either.
            var sourceDirectory = CreateTempDirectory();
            var recovery = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
            var controller = BuildController(
                TestLibraryFilesystemReadiness.Ready(),
                recovery.Object);
            var request = new ManualImportRequestDto
            {
                Path = sourceDirectory,
                Mode = "interactive",
                Action = FileAction.Copy,
                Items =
                [
                    new ManualImportItemDto
                    {
                        FullPath = Path.Join(sourceDirectory, "valid.mp3"),
                        MatchedAudiobookId = 7
                    },
                    new ManualImportItemDto
                    {
                        FullPath = Path.Join(sourceDirectory, "missing-id.mp3")
                    }
                ]
            };

            var action = await controller.Start(request);

            Assert.IsType<BadRequestObjectResult>(action.Result);
            recovery.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Start_ItemWithValidPositiveMatchedAudiobookId_PassesTheNewCheck()
        {
            // A valid id must not be rejected by the new check. To prove that without
            // driving the full import pipeline (locks, recovery, filesystem mutation),
            // the filesystem-mutation gate is left not-ready: Start calls
            // EnsureReady() immediately after item validation and before anything else,
            // and that call is outside Start's own try/catch, so a valid id reaching it
            // surfaces as this specific exception escaping uncaught rather than as a
            // BadRequest.
            var sourceDirectory = CreateTempDirectory();
            var notReadyGate = new TestLibraryFilesystemReadiness();
            notReadyGate.SetRunning("AudiobookFileIdentities");
            var recovery = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
            var controller = BuildController(notReadyGate, recovery.Object);
            var request = new ManualImportRequestDto
            {
                Path = sourceDirectory,
                Mode = "interactive",
                Action = FileAction.Copy,
                Items =
                [
                    new ManualImportItemDto
                    {
                        FullPath = Path.Join(sourceDirectory, "chapter.mp3"),
                        MatchedAudiobookId = 7
                    }
                ]
            };

            var exception = await Assert.ThrowsAsync<ApplicationUnavailableException>(
                async () => await controller.Start(request));

            Assert.Equal("filesystem_initializing", exception.Code);
            recovery.VerifyNoOtherCalls();
        }
    }
}
