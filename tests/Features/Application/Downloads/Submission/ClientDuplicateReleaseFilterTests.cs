using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Downloads.Submission;

[Trait("Name", "ClientDuplicateReleaseFilterTests")]
[Trait("Category", "Application")]
public sealed class ClientDuplicateReleaseFilterTests : BaseTests
{
    private const string InfoHash = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";
    private const string Magnet = $"magnet:?xt=urn:btih:{InfoHash}&dn=book";

    [Fact]
    public void Exclude_DropsTheReleaseTheClientAlreadyHoldsAsADuplicate()
    {
        // The loop: the client declined this release as content it already has, so grabbing it
        // again only gets the same answer.
        var kept = ClientDuplicateReleaseFilter.Exclude(
            [HeldDownload(InfoHash)], [Scored(Magnet)], NullLogger.Instance);

        Assert.Empty(kept);
    }

    [Fact]
    public void Exclude_KeepsADifferentReleaseOfTheSameBook()
    {
        // The hold is per release. If this fails, one duplicate answer takes the book out of
        // automatic search entirely.
        var kept = ClientDuplicateReleaseFilter.Exclude(
            [HeldDownload("0000000000000000000000000000000000000000")], [Scored(Magnet)], NullLogger.Instance);

        Assert.Single(kept);
    }

    [Theory]
    [InlineData(DownloadStatus.Failed, null)]
    [InlineData(DownloadStatus.ImportBlocked, "Unable to import the download")]
    [InlineData(DownloadStatus.Moved, null)]
    public void Exclude_IgnoresDownloadsThatAreNotHeldAsClientDuplicates(
        DownloadStatus status,
        string? blockReason)
    {
        // Control for the test above it: the same release identity on a download that is not a
        // client-duplicate hold must not be excluded, or this filter is a second blocklist.
        var download = HeldDownload(InfoHash);
        download.Status = status;
        download.ImportBlockReason = blockReason;

        var kept = ClientDuplicateReleaseFilter.Exclude([download], [Scored(Magnet)], NullLogger.Instance);

        Assert.Single(kept);
    }

    [Fact]
    public async Task SearchAndDownloadAsync_WhenTheOnlyCandidateIsHeldAsAClientDuplicate_ReportsNoAcceptableResults()
    {
        // The filter's unit tests pass whether or not DownloadService calls it. This one fails if
        // the call is removed.
        var search = new Mock<ISearchService>();
        search.Setup(service => service.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<SearchSortBy>(),
                It.IsAny<SearchSortDirection>(),
                It.IsAny<bool>(),
                It.IsAny<SearchQueryPlan?>()))
            .ReturnsAsync([
                new SearchResult
                {
                    Title = "The Only Listing",
                    MagnetLink = Magnet,
                    Size = 800_000_000,
                    Seeders = 20
                }
            ]);
        _services.AddSingleton(search.Object);
        Init();

        await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
            .WithHost("localhost")
            .WithPort(8080)
            .WithUsername("admin")
            .WithPassword("admin")
            .WithType("qbittorrent")
            .Build());

        var qualityProfile = await _qualityProfileRepository.AddAsync(
            new QualityProfileBuilder().Build());
        var audiobook = await _audiobookRepository.AddAsync(
            new AudiobookBuilder()
                .WithTitle("The Only Listing")
                .WithQualityProfile(qualityProfile)
                .Build());

        var downloadService = _provider.GetRequiredService<IDownloadService>();

        // Establish that the candidate is otherwise selectable, so the assertion below cannot pass
        // for some unrelated reason the search already had to fail.
        var first = await downloadService.SearchAndDownloadAsync(audiobook.Id);
        Assert.True(
            first.Success,
            $"precondition: the candidate must be grabbable when nothing is held, got '{first.Message}'");

        var grabbed = Assert.Single(
            await _downloadRepository.GetAllAsync(),
            download => download.AudiobookId == audiobook.Id);
        grabbed.Blocked(ClientDuplicateHold.Reason, "simulated duplicate answer from the client");
        await _downloadRepository.UpdateAsync(grabbed);

        var result = await downloadService.SearchAndDownloadAsync(audiobook.Id);

        Assert.False(result.Success);
        Assert.Single(
            await _downloadRepository.GetAllAsync(),
            download => download.AudiobookId == audiobook.Id);
    }

    [Fact]
    [Trait("Scenario", "Every path that filters blocked releases also skips client-duplicate holds")]
    public void EveryBlocklistFilteringPath_AlsoSkipsClientDuplicateHolds()
    {
        // The test above covers SearchAndDownloadAsync only. AutomaticSearchService grabs on its
        // own path, which is the one that loops, so the invariant is asserted over every file
        // that filters candidates against the blocklist.
        var root = TestUtils.FindRepositoryRoot();
        var projects = new[] { "listenarr.application", "listenarr.infrastructure", "listenarr.api" };

        var filteringFiles = projects
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Join(root, project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(file => new { File = file, Text = File.ReadAllText(file) })
            .Where(entry => entry.Text.Contains("BlockedReleaseFilter.ExcludeAsync(", StringComparison.Ordinal))
            .ToList();
        var offenders = filteringFiles
            .Where(entry => !entry.Text.Contains("ClientDuplicateReleaseFilter.Exclude(", StringComparison.Ordinal))
            .Select(entry => Path.GetRelativePath(root, entry.File))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        // Both grab paths, so a scan that silently found nothing cannot pass.
        Assert.Equal(2, filteringFiles.Count);
        Assert.True(
            offenders.Length == 0,
            "These filter blocked releases but would re-grab a client-duplicate hold:" + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }

    private static Download HeldDownload(string infoHash)
    {
        var download = new Download { Status = DownloadStatus.Queued };
        download.SetMetadata(ReleaseIdentity.MetadataKey, ReleaseIdentity.For(infoHash, null, null, null)!);
        download.Blocked(ClientDuplicateHold.Reason, "held");
        return download;
    }

    private static QualityScore Scored(string magnet) => new()
    {
        TotalScore = 90,
        SearchResult = new SearchResult { Title = "Book", MagnetLink = magnet }
    };
}
