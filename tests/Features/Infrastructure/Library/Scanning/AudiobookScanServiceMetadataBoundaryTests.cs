using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Scanning;

[Trait("Name", "AudiobookScanServiceMetadataBoundaryTests")]
[Trait("Category", "Infrastructure")]
public sealed class AudiobookScanServiceMetadataBoundaryTests : BaseTests
{
    [LinuxFact]
    public async Task ScanAsync_CaseDistinctMetadataFolders_RemainConflicting()
    {
        var root = FileService.GetTempDirectory("scan-service-case-distinct-metadata");
        var initialResolution = await _provider
            .GetRequiredService<IFileSystemSemanticsResolver>()
            .ResolveAsync(root);
        Assert.Equal(
            FileSystemCaseSensitivity.Sensitive,
            initialResolution.Semantics.CaseSensitivity);

        var upperDirectory = Path.Join(root, "Metadata Book");
        var lowerDirectory = Path.Join(root, "metadata book");
        var upperFile = Path.Join(upperDirectory, "part-a.m4b");
        var lowerFile = Path.Join(lowerDirectory, "part-b.m4b");
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .ReturnsAsync(MatchingMetadata());
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
        Directory.CreateDirectory(upperDirectory);
        Directory.CreateDirectory(lowerDirectory);
        await File.WriteAllTextAsync(upperFile, "audio");
        await File.WriteAllTextAsync(lowerFile, "audio");
        await _applicationSettingsRepository.SaveAsync(
            new ApplicationSettingsBuilder()
                .WithOutputPath(FileService.GetTempPath())
                .Build());
        var audiobook = await _audiobookRepository.AddAsync(
            new AudiobookBuilder()
                .WithTitle("Expected Title")
                .WithAuthor("Expected Author")
                .Build());
        var authorization = await _provider
            .GetRequiredService<IScanPathAuthorizationService>()
            .AuthorizeAsync(root);
        Assert.True(authorization.IsAuthorized, authorization.Error);
        var pathIdentity = Assert.IsType<PathIdentitySnapshot>(authorization.Identity);
        var physicalIdentity = Assert.IsType<ScanPathPhysicalIdentity>(
            authorization.PhysicalIdentity);
        Assert.Equal(
            FileSystemCaseSensitivity.Sensitive,
            pathIdentity.CaseSensitivity);

        var result = await _provider
            .GetRequiredService<IAudiobookScanService>()
            .ScanAsync(new AudiobookScanCommand(
                audiobook.Id,
                root,
                pathIdentity,
                physicalIdentity));

        Assert.Empty(result.AttributedFiles);
        Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));
        Assert.Null(result.Audiobook.BasePath);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataAttributionConflict");
        metadata.Verify(
            service => service.ExtractFileMetadataAsync(It.IsAny<MetadataFileSource>()),
            Times.Exactly(2));
    }

    [LinuxFact]
    public async Task ScanAsync_DeclinedFallbackMetadata_RecordsDiagnosticNamingTheCandidate()
    {
        var root = FileService.GetTempDirectory("scan-service-declined-metadata");
        var bookDirectory = Path.Join(root, "Some Book");
        var candidate = Path.Join(bookDirectory, "Part 01.m4b");
        var readPaths = new List<string>();
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        // What MetadataService returns after ffprobe throws: a filename-only fallback,
        // non-null, which the attribution check then declines.
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .Callback((MetadataFileSource source) => readPaths.Add(source.ReadPath))
            .ReturnsAsync(new AudioMetadata { Title = "Part 01", Format = "M4B" });
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
        Directory.CreateDirectory(bookDirectory);
        await File.WriteAllTextAsync(candidate, "audio");
        await _applicationSettingsRepository.SaveAsync(
            new ApplicationSettingsBuilder()
                .WithOutputPath(FileService.GetTempPath())
                .Build());
        var audiobook = await _audiobookRepository.AddAsync(
            new AudiobookBuilder()
                .WithTitle("Expected Title")
                .WithAuthor("Expected Author")
                .Build());
        var authorization = await _provider
            .GetRequiredService<IScanPathAuthorizationService>()
            .AuthorizeAsync(root);
        Assert.True(authorization.IsAuthorized, authorization.Error);

        var result = await _provider
            .GetRequiredService<IAudiobookScanService>()
            .ScanAsync(new AudiobookScanCommand(
                audiobook.Id,
                root,
                Assert.IsType<PathIdentitySnapshot>(authorization.Identity),
                Assert.IsType<ScanPathPhysicalIdentity>(authorization.PhysicalIdentity)));

        Assert.Empty(result.AttributedFiles);
        Assert.Equal(1, result.DiscoveredCandidateCount);
        // Control: the byte source really was a descriptor, so a Path equal to the
        // candidate shows the diagnostic chose the public identity.
        var readPath = Assert.Single(readPaths);
        Assert.StartsWith("/proc/", readPath, StringComparison.Ordinal);
        var declined = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataDeclined");
        Assert.Equal(candidate, declined.Path);
        Assert.DoesNotContain("/proc/", declined.Message, StringComparison.Ordinal);
    }

    [LinuxFact]
    public async Task ScanAsync_RootHoldsOnlyAnotherBooksFile_IsNotCountedOrDeclined()
    {
        var root = FileService.GetTempDirectory("scan-service-other-owner");
        var ownerDirectory = Path.Join(root, "Owner Folder");
        var ownedFile = Path.Join(ownerDirectory, "part-a.m4b");
        var readPublicPaths = new List<string>();
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .Callback((MetadataFileSource source) => readPublicPaths.Add(source.PublicPath))
            .ReturnsAsync(MatchingMetadata());
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
        Directory.CreateDirectory(ownerDirectory);
        await File.WriteAllTextAsync(ownedFile, "audio");
        await _applicationSettingsRepository.SaveAsync(
            new ApplicationSettingsBuilder()
                .WithOutputPath(FileService.GetTempPath())
                .Build());
        var owner = await _audiobookRepository.AddAsync(
            new AudiobookBuilder()
                .WithTitle("Expected Title")
                .WithAuthor("Expected Author")
                .Build());
        var other = await _audiobookRepository.AddAsync(
            new AudiobookBuilder()
                .WithTitle("Unrelated Title")
                .WithAuthor("Unrelated Author")
                .Build());
        var scanService = _provider.GetRequiredService<IAudiobookScanService>();

        // Control: the owner claims its file through embedded metadata.
        var ownerResult = await scanService.ScanAsync(await AuthorizedCommandAsync(
            owner.Id,
            ownerDirectory));
        Assert.Equal(ownedFile, Assert.Single(ownerResult.AttributedFiles));
        Assert.Equal(1, ownerResult.CreatedCount);
        Assert.Equal(1, ownerResult.DiscoveredCandidateCount);
        readPublicPaths.Clear();

        var otherResult = await scanService.ScanAsync(await AuthorizedCommandAsync(
            other.Id,
            root));

        Assert.Empty(otherResult.AttributedFiles);
        Assert.Equal(0, otherResult.DiscoveredCandidateCount);
        Assert.DoesNotContain(otherResult.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataDeclined");
        Assert.Empty(readPublicPaths);
        Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(owner.Id));
    }

    private async Task<AudiobookScanCommand> AuthorizedCommandAsync(
        int audiobookId,
        string scanRoot)
    {
        var authorization = await _provider
            .GetRequiredService<IScanPathAuthorizationService>()
            .AuthorizeAsync(scanRoot);
        Assert.True(authorization.IsAuthorized, authorization.Error);
        return new AudiobookScanCommand(
            audiobookId,
            scanRoot,
            Assert.IsType<PathIdentitySnapshot>(authorization.Identity),
            Assert.IsType<ScanPathPhysicalIdentity>(authorization.PhysicalIdentity));
    }

    private static AudioMetadata MatchingMetadata() => new()
    {
        Title = "Expected Title",
        Artist = "Expected Author",
        Duration = TimeSpan.FromSeconds(1),
        Format = "m4b"
    };
}
