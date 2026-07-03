namespace Listenarr.Tests.Features.Domain.Audiobooks;

public sealed class RootFolderRelocationStateTests
{
    [Fact]
    public void NewRoot_RequiresResolvedIdentityBeforeDestructiveWork()
    {
        var root = new RootFolder();

        Assert.Equal(FileSystemCaseSensitivityMode.Auto, root.CaseSensitivityMode);
        Assert.Equal(FileSystemCaseSensitivity.Unknown, root.ResolvedCaseSensitivity);
        Assert.Equal(PathIdentityState.Unavailable, root.PathIdentityState);
        Assert.Null(root.PathIdentityKey);
    }

    [Fact]
    public void NewRelocation_HoldsPendingPathWithoutChangingRoot()
    {
        var relocation = new RootFolderRelocation
        {
            SourcePath = "/library",
            TargetPath = "/new-library"
        };

        Assert.Equal(RootFolderRelocationStatus.Pending, relocation.Status);
        Assert.Equal("/new-library", relocation.TargetPath);
    }
}
