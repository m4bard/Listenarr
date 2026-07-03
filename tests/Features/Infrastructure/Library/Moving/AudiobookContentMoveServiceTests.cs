using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving
{
    [Trait("Name", "AudiobookContentMoveServiceTests")]
    [Trait("Category", "BackgroundWorkers")]
    public class AudiobookContentMoveServiceTests : BaseTests
    {
        [Fact]
        public async Task MoveContentsAsync_NormalMove_MovesContentsAndDeletesSource()
        {
            var source = FileService.GetTempDirectory("content-move-normal-src");
            await FileService.GetFileAsync(source, "book.m4b", "audio");
            var extras = Path.Join(source, "extras");
            Directory.CreateDirectory(extras);
            await FileService.GetFileAsync(extras, "cover.jpg", "image");
            var target = Path.Join(FileService.GetTempPath(), $"content-move-normal-dst-{Guid.NewGuid():N}");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            var result = await service.MoveContentsAsync(new AudiobookContentMoveRequest(source, target, Guid.NewGuid()), CancellationToken.None);

            Assert.Equal(Path.GetFullPath(source), result.Source);
            Assert.Equal(Path.GetFullPath(target), result.Target);
            Assert.False(result.TargetInsideSource);
            Assert.False(result.SourceInsideTarget);
            Assert.False(Directory.Exists(source));
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
            Assert.True(File.Exists(Path.Join(target, "extras", "cover.jpg")));
        }

        [Fact]
        public async Task MoveContentsAsync_JobTempContainsPartialFile_ReplacesItOnRetry()
        {
            var source = FileService.GetTempDirectory("content-move-partial-src");
            await FileService.GetFileAsync(source, "book.m4b", "complete audio");
            var target = Path.Join(FileService.GetTempPath(), $"content-move-partial-dst-{Guid.NewGuid():N}");
            var jobId = Guid.NewGuid();
            var targetParent = Path.GetDirectoryName(target)!;
            var tempName = Path.Join(targetParent, Path.GetFileName(target) + ".tmp-" + jobId.ToString("N"));
            Directory.CreateDirectory(tempName);
            await File.WriteAllTextAsync(Path.Join(tempName, "book.m4b"), "partial");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            await service.MoveContentsAsync(new AudiobookContentMoveRequest(source, target, jobId), CancellationToken.None);

            Assert.False(Directory.Exists(source));
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
            Assert.Equal("complete audio", await File.ReadAllTextAsync(Path.Join(target, "book.m4b")));
        }

        [Fact]
        public async Task MoveContentsAsync_TargetInsideSource_MovesContentsIntoChildAndKeepsTarget()
        {
            var source = FileService.GetTempDirectory("content-move-child-src");
            await FileService.GetFileAsync(source, "book.m4b", "audio");
            var extras = Path.Join(source, "extras");
            Directory.CreateDirectory(extras);
            await FileService.GetFileAsync(extras, "cover.jpg", "image");
            var target = Path.Join(source, " test");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            var result = await service.MoveContentsAsync(new AudiobookContentMoveRequest(source, target, Guid.NewGuid()), CancellationToken.None);

            Assert.True(result.TargetInsideSource);
            Assert.False(result.SourceInsideTarget);
            Assert.True(Directory.Exists(source));
            Assert.True(Directory.Exists(target));
            Assert.False(File.Exists(Path.Join(source, "book.m4b")));
            Assert.False(Directory.Exists(extras));
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
            Assert.True(File.Exists(Path.Join(target, "extras", "cover.jpg")));
        }

        [Fact]
        public async Task MoveContentsAsync_TargetDeepInsideSource_RemovesSiblingContentFromTargetAncestors()
        {
            var source = FileService.GetTempDirectory("content-move-deep-child-src");
            await FileService.GetFileAsync(source, "book.m4b", "audio");
            var targetAncestor = Path.Join(source, "container");
            Directory.CreateDirectory(targetAncestor);
            await FileService.GetFileAsync(targetAncestor, "stale-sibling.txt", "stale");
            var target = Path.Join(targetAncestor, "target");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            await service.MoveContentsAsync(new AudiobookContentMoveRequest(source, target, Guid.NewGuid()), CancellationToken.None);

            Assert.True(Directory.Exists(source));
            Assert.True(Directory.Exists(target));
            Assert.False(File.Exists(Path.Join(source, "book.m4b")));
            Assert.False(File.Exists(Path.Join(targetAncestor, "stale-sibling.txt")));
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
        }

        [Fact]
        public async Task MoveContentsAsync_SourceInsideTarget_MovesContentsUpAndDeletesOldChild()
        {
            var target = FileService.GetTempDirectory("content-move-parent-target");
            var source = Path.Join(target, " test");
            Directory.CreateDirectory(source);
            await FileService.GetFileAsync(source, "book.m4b", "audio");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            var result = await service.MoveContentsAsync(new AudiobookContentMoveRequest(source, target, Guid.NewGuid()), CancellationToken.None);

            Assert.False(result.TargetInsideSource);
            Assert.True(result.SourceInsideTarget);
            Assert.True(Directory.Exists(target));
            Assert.False(Directory.Exists(source));
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
        }

        [Fact]
        public async Task MoveContentsAsync_SourceInsideTarget_WithUnrelatedSibling_Fails()
        {
            var target = FileService.GetTempDirectory("content-move-parent-with-sibling-target");
            var source = Path.Join(target, "Title");
            Directory.CreateDirectory(source);
            await FileService.GetFileAsync(source, "book.m4b", "audio");
            var sibling = Path.Join(target, "OtherBook");
            Directory.CreateDirectory(sibling);
            await FileService.GetFileAsync(sibling, "other.m4b", "other");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            var ex = await Assert.ThrowsAsync<IOException>(() => service.MoveContentsAsync(new AudiobookContentMoveRequest(source, target, Guid.NewGuid()), CancellationToken.None));

            Assert.Contains("unrelated content", ex.Message);
            Assert.True(File.Exists(Path.Join(source, "book.m4b")));
            Assert.True(File.Exists(Path.Join(sibling, "other.m4b")));
        }

        [Fact]
        public async Task MoveContentsAsync_SourceInsideEmptyParent_DeletesEmptyParentAfterMove()
        {
            var sourceParent = FileService.GetTempDirectory("content-move-empty-parent");
            var source = Path.Join(sourceParent, " test");
            Directory.CreateDirectory(source);
            await FileService.GetFileAsync(source, "book.m4b", "audio");
            var target = Path.Join(FileService.GetTempPath(), $"content-move-cleaned-dst-{Guid.NewGuid():N}");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            await service.MoveContentsAsync(new AudiobookContentMoveRequest(source, target, Guid.NewGuid()), CancellationToken.None);

            Assert.False(Directory.Exists(source));
            Assert.False(Directory.Exists(sourceParent));
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
        }

        [Fact]
        public async Task MoveContentsAsync_SourceInsideNonEmptyParent_DoesNotDeleteParent()
        {
            var sourceParent = FileService.GetTempDirectory("content-move-nonempty-parent");
            await FileService.GetFileAsync(sourceParent, "keep.txt", "keep");
            var source = Path.Join(sourceParent, " test");
            Directory.CreateDirectory(source);
            await FileService.GetFileAsync(source, "book.m4b", "audio");
            var target = Path.Join(FileService.GetTempPath(), $"content-move-nonempty-dst-{Guid.NewGuid():N}");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            await service.MoveContentsAsync(new AudiobookContentMoveRequest(source, target, Guid.NewGuid()), CancellationToken.None);

            Assert.False(Directory.Exists(source));
            Assert.True(Directory.Exists(sourceParent));
            Assert.True(File.Exists(Path.Join(sourceParent, "keep.txt")));
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
        }

        [Fact]
        public async Task MoveContentsAsync_TargetContainsUnrelatedFiles_Fails()
        {
            var source = FileService.GetTempDirectory("content-move-collision-src");
            await FileService.GetFileAsync(source, "book.m4b", "audio");
            var target = FileService.GetTempDirectory("content-move-collision-dst");
            await FileService.GetFileAsync(target, "existing.txt", "blocked");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            var ex = await Assert.ThrowsAsync<IOException>(() => service.MoveContentsAsync(new AudiobookContentMoveRequest(source, target, Guid.NewGuid()), CancellationToken.None));

            Assert.Contains("contains files", ex.Message);
            Assert.True(Directory.Exists(source));
            Assert.True(File.Exists(Path.Join(source, "book.m4b")));
            Assert.True(File.Exists(Path.Join(target, "existing.txt")));
        }

        [Fact]
        public async Task MoveContentsAsync_TargetContainsOnlySourceSubtree_AllowsMove()
        {
            var target = FileService.GetTempDirectory("content-move-source-subtree-target");
            var source = Path.Join(target, "nested", "source");
            Directory.CreateDirectory(source);
            await FileService.GetFileAsync(source, "book.m4b", "audio");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            await service.MoveContentsAsync(new AudiobookContentMoveRequest(source, target, Guid.NewGuid()), CancellationToken.None);

            Assert.True(Directory.Exists(target));
            Assert.False(Directory.Exists(source));
            Assert.False(Directory.Exists(Path.Join(target, "nested")));
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
        }

        [Fact]
        public async Task MoveContentsAsync_TargetInsideSource_TargetAlreadyContainsFile_Fails()
        {
            var source = FileService.GetTempDirectory("content-move-child-collision-src");
            await FileService.GetFileAsync(source, "book.m4b", "audio");
            var target = Path.Join(source, " test");
            Directory.CreateDirectory(target);
            await FileService.GetFileAsync(target, "existing.txt", "blocked");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            var ex = await Assert.ThrowsAsync<IOException>(() => service.MoveContentsAsync(new AudiobookContentMoveRequest(source, target, Guid.NewGuid()), CancellationToken.None));

            Assert.Contains("contains files", ex.Message);
            Assert.True(Directory.Exists(source));
            Assert.True(File.Exists(Path.Join(source, "book.m4b")));
            Assert.True(File.Exists(Path.Join(target, "existing.txt")));
        }
    }
}
