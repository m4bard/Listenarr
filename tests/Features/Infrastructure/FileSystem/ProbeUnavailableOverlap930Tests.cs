using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

[Trait("Area", "FileSystem")]
[Trait("Name", "ProbeUnavailableOverlap930Tests")]
public sealed class ProbeUnavailableOverlap930Tests : BaseTests
{
    [DirectoryLinkFact]
    public async Task ProbeE_OnlyAnUnavailableRootCoversTheSource_ReachedThroughALink()
    {
        var directory = FileService.GetTempDirectory("probe-e-unavailable-overlap");
        var physicalRoot = Directory.CreateDirectory(Path.Join(directory, "physical-library")).FullName;
        var linkedRoot = Path.Join(directory, "library");
        Directory.CreateSymbolicLink(linkedRoot, physicalRoot);
        var bookDirectory = Directory.CreateDirectory(Path.Join(linkedRoot, "Book")).FullName;
        var source = Path.Join(bookDirectory, "book.m4b");
        await File.WriteAllTextAsync(source, "audio");

        var dbContextFactory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using (var db = await dbContextFactory.CreateDbContextAsync())
        {
            db.RootFolders.Add(new RootFolder
            {
                Name = "Unavailable linked root",
                Path = linkedRoot,
                CaseSensitivityMode = FileSystemCaseSensitivityMode.Auto,
                PathIdentityState = PathIdentityState.Unavailable,
                ResolvedCaseSensitivity = FileSystemCaseSensitivity.Unknown
            });
            await db.SaveChangesAsync();
        }

        var mover = new FileMover(
            NullLogger<FileMover>.Instance,
            dbContextFactory: dbContextFactory,
            rootFolderRepository: _rootFolderRepository);

        var result = await mover.CheckAsync(source);
        File.WriteAllText(Path.Join(Path.GetTempPath(), "probe-e-reason-" + Environment.GetEnvironmentVariable("PROBE_LABEL") + ".txt"),
            $"supported={result.IsSupported} reason={result.Reason}");

        Assert.False(result.IsSupported);
        Assert.DoesNotContain("symbolic link", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
