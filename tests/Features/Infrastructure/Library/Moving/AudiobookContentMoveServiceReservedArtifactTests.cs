namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class AudiobookContentMoveServiceTests
{
    [Fact]
    public async Task MoveContentsAsync_SourceContainsOldRecoveryMarker_PreservesAndRequiresAttention()
    {
        var source = FileService.GetTempDirectory("content-move-old-marker-src");
        await FileService.GetFileAsync(source, "book.m4b", "audio");
        var oldMarker = await FileService.GetFileAsync(
            source,
            $".listenarr-move-{Guid.NewGuid():N}.pending",
            "obsolete recovery evidence");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-old-marker-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();

        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.MoveContentsAsync(request, CancellationToken.None));

        Assert.Contains("reserved Listenarr recovery artifact", exception.Message);
        Assert.Equal("obsolete recovery evidence", await File.ReadAllTextAsync(oldMarker));
        Assert.False(Directory.Exists(target));
    }
}
