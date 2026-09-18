using System.Text.RegularExpressions;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Submission;

/// <summary>
/// The unit tests for BlockedReleaseFilter pass whether or not DownloadService calls it.
/// This one fails if the call is removed, which is the point of having it.
/// </summary>
[Trait("Name", "BlocklistSearchWiringTests")]
[Trait("Category", "Application")]
public sealed class BlocklistSearchWiringTests : BaseTests
{
    private const string InfoHash = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    // The funnel and its alias, both. StartDownloadAsync (DownloadService.cs:53-56) is a two-line
    // pass-through to SendToDownloadClientAsync (:229, :237), which is where a release actually
    // reaches a download client. Watching only the alias left the method spelling exposed the same
    // way watching a receiver name left the variable spelling exposed: SendToDownloadClientAsync
    // already has a caller the alias-only pattern could not see, and a tidy-up that removed the
    // alias would have left this matching zero files and passing on that.
    private static readonly Regex GrabCall = new(
        @"\.\s*(?:StartDownloadAsync|SendToDownloadClientAsync)\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex FilterCall = new(
        @"BlockedReleaseFilter\s*\.\s*ExcludeAsync\s*\(", RegexOptions.CultureInvariant);

    // The manual send-to-client endpoint, deliberately unfiltered. An operator who picks one
    // release out of an interactive search and sends it to a client is overriding the automatic
    // choice on purpose, and an override the blocklist could veto would not be one.
    //
    // No claim here about what the operator sees first. Readarr can afford one, because its
    // blocklist is a decision-engine rejection and a blocklisted release stays in the interactive
    // result with its reason showing. This filter drops candidates before scoring instead
    // (BlockedReleaseFilter.ExcludeAsync returns only what it kept) and there is no frontend
    // surface, so an automatic search ends at "no acceptable search results" and one log line.
    // Closing that gap is the family's shape and worth doing; it is not what this list is about.
    //
    // Listed rather than pattern-excluded so that adding a second unfiltered path is a decision
    // somebody has to write down here.
    private static readonly string[] DeliberatelyUnfiltered =
    [
        Path.Join("listenarr.api", "Features", "Downloads", "DownloadController.cs")
    ];

    [Fact]
    public async Task SearchAndDownloadAsync_WhenTheOnlyCandidateIsBlocked_ReportsNoAcceptableResults()
    {
        var search = new Mock<ISearchService>();
        search.Setup(service => service.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<SearchSortBy>(),
                It.IsAny<SearchSortDirection>(),
                It.IsAny<bool>()))
            .ReturnsAsync([
                new SearchResult
                {
                    Title = "The Only Listing",
                    MagnetLink = $"magnet:?xt=urn:btih:{InfoHash}&dn=book",
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
                .WithTitle("Blocked Book")
                .WithQualityProfile(qualityProfile)
                .Build());

        var downloadService = _provider.GetRequiredService<IDownloadService>();

        // Establish that this candidate is otherwise selectable. Without this the assertion
        // below passes for whatever reason the search already had to fail, which is how a
        // filter that is never called still looks green.
        var unblocked = await downloadService.SearchAndDownloadAsync(audiobook.Id);
        Assert.True(
            unblocked.Success,
            $"precondition: the candidate must be grabbable when nothing is blocked, got '{unblocked.Message}'");

        foreach (var created in (await _downloadRepository.GetAllAsync())
                 .Where(download => download.AudiobookId == audiobook.Id))
        {
            await _downloadRepository.RemoveAsync(created.Id);
        }

        var blocklist = _provider.GetRequiredService<IBlocklistService>();
        await blocklist.BlockAsync(
            audiobook.Id,
            ReleaseIdentity.KeyFor(InfoHash, null)!,
            "The Only Listing",
            800_000_000,
            "simulated earlier failure");

        var result = await downloadService.SearchAndDownloadAsync(audiobook.Id);

        Assert.False(result.Success);
        Assert.DoesNotContain(
            await _downloadRepository.GetAllAsync(),
            download => download.AudiobookId == audiobook.Id);
    }

    [Fact]
    [Trait("Scenario", "Every path that grabs a release consults the blocklist")]
    public void EveryGrabPath_ConsultsTheBlocklist()
    {
        // The test above only covers SearchAndDownloadAsync. That is one of two paths that actually
        // grab, and the blocklist was wired into it alone: AutomaticSearchService scores results and
        // calls StartDownloadAsync directly, so a release could fail, be blocked, and be grabbed
        // again by the next automatic pass a minute later. A live install looped for hours that way
        // while every unit test here passed.
        //
        // This asserts the invariant rather than one instance of it: a production file that starts a
        // download has to consult the blocklist somewhere in the same file.
        //
        // Both halves of that are matched on shape rather than on a spelling. Looking for
        // "await downloadService.StartDownloadAsync(" and "await _downloadService." by name
        // misses a third grab path the moment the variable is called something else, which is
        // precisely the bug this test exists to have caught. And accepting a file because the
        // text "IBlocklistService" appears in it anywhere passes for an unused using directive or
        // an injected-but-never-called constructor parameter. So: any receiver, and the filter
        // call itself.
        var root = TestUtils.FindRepositoryRoot();
        var projects = new[] { "listenarr.application", "listenarr.infrastructure", "listenarr.api" };

        var grabPaths = projects
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Join(root, project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(file => new { Relative = Path.GetRelativePath(root, file), Text = File.ReadAllText(file) })
            // A call on some receiver, not a declaration: a file that only defines or declares one
            // of these is not a grab path.
            .Where(entry => GrabCall.IsMatch(entry.Text))
            .OrderBy(entry => entry.Relative, StringComparer.Ordinal)
            .ToArray();

        // A guard whose pass state is "found no offenders" is indistinguishable from a guard that
        // matched nothing at all, and the second is how a pattern pointed at a renamed or deleted
        // method keeps reading green. So the sweep has to find the grab paths first.
        Assert.NotEmpty(grabPaths);

        var offenders = grabPaths
            .Where(entry => !FilterCall.IsMatch(entry.Text))
            .Select(entry => entry.Relative)
            .Where(relative => !DeliberatelyUnfiltered.Contains(relative, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These start a download without consulting the blocklist:" + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }
}
