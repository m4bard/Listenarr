/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Tests.Builders;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Jobs
{
    [Trait("Area", "Jobs")]
    [Trait("Name", "ScanQueueSemanticsTests")]
    public sealed class ScanQueueSemanticsTests
    {
        [Theory]
        [InlineData(FileSystemCaseSensitivity.Sensitive, false)]
        [InlineData(FileSystemCaseSensitivity.Insensitive, true)]
        public async Task ScanQueue_DedupeUsesResolvedSemantics(
            FileSystemCaseSensitivity caseSensitivity,
            bool shouldDedupe)
        {
            var queue = new ScanQueueService(
                NullLogger<ScanQueueService>.Instance,
                BuildResolver(caseSensitivity));
            var audiobook = new AudiobookBuilder()
                .WithId(1001)
                .WithTitle("Case Book")
                .Build();
            var root = Path.GetFullPath(Path.Join(Path.GetTempPath(), "listenarr-scan-queue"));
            var first = Path.Join(root, "CaseBook");
            var second = Path.Join(root, "casebook");

            var firstJob = await queue.EnqueueScanAsync(audiobook, first);
            var secondJob = await queue.EnqueueScanAsync(audiobook, second);

            Assert.Equal(shouldDedupe, firstJob == secondJob);
        }

        [Theory]
        [InlineData(FileSystemCaseSensitivity.Sensitive, false)]
        [InlineData(FileSystemCaseSensitivity.Insensitive, true)]
        public async Task UnmatchedScanQueue_DedupeUsesResolvedSemantics(
            FileSystemCaseSensitivity caseSensitivity,
            bool shouldDedupe)
        {
            var queue = new UnmatchedScanQueueService(
                NullLogger<UnmatchedScanQueueService>.Instance,
                BuildResolver(caseSensitivity));
            var root = Path.GetFullPath(Path.Join(Path.GetTempPath(), "listenarr-unmatched-queue"));
            var first = Path.Join(root, "CaseRoot");
            var second = Path.Join(root, "caseroot");

            var firstJob = await queue.EnqueueAsync(first);
            var secondJob = await queue.EnqueueAsync(second);

            Assert.Equal(shouldDedupe, firstJob == secondJob);
        }

        private static IFileSystemSemanticsResolver BuildResolver(FileSystemCaseSensitivity caseSensitivity)
        {
            var resolver = new Mock<IFileSystemSemanticsResolver>();
            resolver.Setup(r => r.ResolveAsync(
                    It.IsAny<string>(),
                    It.IsAny<FileSystemCaseSensitivityMode>(),
                    It.IsAny<CancellationToken>()))
                .Returns<string, FileSystemCaseSensitivityMode, CancellationToken>((path, _, _) =>
                    ValueTask.FromResult(new FileSystemSemanticsResolution(
                        new FileSystemPathSemantics(FileSystemPathSemantics.CurrentHostDefault.Syntax, caseSensitivity),
                        PathIdentityState.Valid,
                        path)));
            return resolver.Object;
        }
    }
}
