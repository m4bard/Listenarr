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
        // The other book's file is still probed (it guards metadata attribution
        // conflicts) but is neither counted nor reported as declined.
        Assert.Equal(ownedFile, Assert.Single(readPublicPaths));
        Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(owner.Id));
    }

    [LinuxFact]
    public async Task ScanAsync_MetadataMatchesForeignOwnedAndUnownedFolders_StaysConflicting()
    {
        var root = FileService.GetTempDirectory("scan-service-foreign-metadata-conflict");
        var ownedDirectory = Path.Join(root, "Folder A");
        var unownedDirectory = Path.Join(root, "Folder B");
        var ownedFile = Path.Join(ownedDirectory, "part-a.m4b");
        var unownedFile = Path.Join(unownedDirectory, "part-b.m4b");
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .ReturnsAsync(MatchingMetadata());
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
        Directory.CreateDirectory(ownedDirectory);
        Directory.CreateDirectory(unownedDirectory);
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
        var target = await _audiobookRepository.AddAsync(
            new AudiobookBuilder()
                .WithTitle("Expected Title")
                .WithAuthor("Expected Author")
                .WithBasePath(root)
                .Build());
        var scanService = _provider.GetRequiredService<IAudiobookScanService>();
        var ownerResult = await scanService.ScanAsync(await AuthorizedCommandAsync(
            owner.Id,
            ownedDirectory));
        Assert.Equal(1, ownerResult.CreatedCount);
        await File.WriteAllTextAsync(unownedFile, "audio");

        var result = await scanService.ScanAsync(await AuthorizedCommandAsync(
            target.Id,
            root));

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataAttributionConflict");
        Assert.Equal(0, result.CreatedCount);
        Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(target.Id));
        Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(owner.Id));
    }

    [LinuxFact]
    public async Task ScanAsync_IdentifierFolderHoldsAnotherBooksTaggedFile_DeclinesAndClaimsNothing()
    {
        // The folder name carries the book's ASIN, so Discover() attributes the file on the
        // folder alone. Its embedded tags name a different book by a different author, and
        // that content evidence must override the folder match.
        var bookDirectory = Path.Join(
            FileService.GetTempDirectory("scan-service-wrong-book-tags"),
            "Expected Title [B012345678]");
        var candidate = Path.Join(bookDirectory, "borrowed.m4b");
        var probedPublicPaths = new List<string>();
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .Callback((MetadataFileSource source) => probedPublicPaths.Add(source.PublicPath))
            .ReturnsAsync(new AudioMetadata
            {
                Title = "Unrelated Title",
                Album = "Unrelated Title",
                Artist = "Unrelated Author",
                AlbumArtist = "Unrelated Author",
                Duration = TimeSpan.FromSeconds(1),
                Format = "m4b"
            });
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
        Directory.CreateDirectory(bookDirectory);
        await File.WriteAllTextAsync(candidate, "audio");
        await _applicationSettingsRepository.SaveAsync(
            new ApplicationSettingsBuilder()
                .WithOutputPath(FileService.GetTempPath())
                .Build());
        var audiobookToAdd = new AudiobookBuilder()
            .WithTitle("Expected Title")
            .WithAuthor("Expected Author")
            .Build();
        audiobookToAdd.Asin = "B012345678";
        var audiobook = await _audiobookRepository.AddAsync(audiobookToAdd);
        var command = await AuthorizedCommandAsync(audiobook.Id, bookDirectory);
        // Control: metadata enrichment is not skipped for limited storage here, so a pass
        // cannot come from the enrichment gate rather than the content check.
        Assert.True(command.ScanPhysicalIdentity.HasDurableGenerationProof);

        var result = await _provider
            .GetRequiredService<IAudiobookScanService>()
            .ScanAsync(command);

        Assert.Empty(result.AttributedFiles);
        Assert.Equal(0, result.CreatedCount);
        Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));
        Assert.Equal(1, result.DiscoveredCandidateCount);
        Assert.False(result.HasDurableAttributedOwnership);
        Assert.Null(result.Audiobook.BasePath);
        var contradiction = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataContradictsPath");
        Assert.Equal(candidate, contradiction.Path);
        Assert.DoesNotContain("/proc/", contradiction.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataDeclined");
        Assert.Equal(candidate, Assert.Single(probedPublicPaths));
    }

    [LinuxTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScanAsync_BookBoundaryOrExactFileNameHoldsAnotherBooksTaggedFile_DeclinesAndClaimsNothing(
        bool viaTitleFolderBoundary)
    {
        // The same wrong-book-tags scenario as the stable-identifier case, reached
        // through the other two folder/filename evidence kinds instead: a title-named
        // subfolder under the author (BookBoundary), or an exact-filename match with
        // author context from the scan root itself (ExactFileName). No ASIN anywhere.
        var authorDirectory = Path.Join(
            FileService.GetTempDirectory("scan-service-wrong-book-tags-boundary"),
            "Expected Author");
        string scanRoot;
        string candidate;
        if (viaTitleFolderBoundary)
        {
            var bookDirectory = Path.Join(authorDirectory, "Expected Title");
            Directory.CreateDirectory(bookDirectory);
            candidate = Path.Join(bookDirectory, "borrowed.m4b");
            scanRoot = bookDirectory;
        }
        else
        {
            Directory.CreateDirectory(authorDirectory);
            candidate = Path.Join(authorDirectory, "Expected Title.m4b");
            scanRoot = authorDirectory;
        }

        var probedPublicPaths = new List<string>();
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .Callback((MetadataFileSource source) => probedPublicPaths.Add(source.PublicPath))
            .ReturnsAsync(new AudioMetadata
            {
                Title = "Unrelated Title",
                Album = "Unrelated Title",
                Artist = "Unrelated Author",
                AlbumArtist = "Unrelated Author",
                Duration = TimeSpan.FromSeconds(1),
                Format = "m4b"
            });
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
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

        var result = await _provider
            .GetRequiredService<IAudiobookScanService>()
            .ScanAsync(await AuthorizedCommandAsync(audiobook.Id, scanRoot));

        Assert.Empty(result.AttributedFiles);
        Assert.Equal(0, result.CreatedCount);
        Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));
        var contradiction = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataContradictsPath");
        Assert.Equal(candidate, contradiction.Path);
        Assert.Equal(candidate, Assert.Single(probedPublicPaths));
    }

    [LinuxFact]
    public async Task ScanAsync_IdentifierFolderUntaggedFile_IsStillClaimed()
    {
        // The critical non-regression case: an untagged file whose only "metadata" is
        // the ffprobe filename fallback (Title == stem, nothing else) must keep being
        // claimed on folder evidence alone, exactly as before this change.
        var bookDirectory = Path.Join(
            FileService.GetTempDirectory("scan-service-untagged-file"),
            "Expected Title [B012345678]");
        var candidate = Path.Join(bookDirectory, "track01.m4b");
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .ReturnsAsync(new AudioMetadata
            {
                Title = "track01",
                Duration = TimeSpan.FromSeconds(1),
                Format = "m4b"
            });
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
        Directory.CreateDirectory(bookDirectory);
        await File.WriteAllTextAsync(candidate, "audio");
        await _applicationSettingsRepository.SaveAsync(
            new ApplicationSettingsBuilder()
                .WithOutputPath(FileService.GetTempPath())
                .Build());
        var audiobookToAdd = new AudiobookBuilder()
            .WithTitle("Expected Title")
            .WithAuthor("Expected Author")
            .Build();
        audiobookToAdd.Asin = "B012345678";
        var audiobook = await _audiobookRepository.AddAsync(audiobookToAdd);

        var result = await _provider
            .GetRequiredService<IAudiobookScanService>()
            .ScanAsync(await AuthorizedCommandAsync(audiobook.Id, bookDirectory));

        Assert.Equal(candidate, Assert.Single(result.AttributedFiles));
        Assert.Equal(1, result.CreatedCount);
        Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataContradictsPath");
    }

    [LinuxFact]
    public async Task ScanAsync_IdentifierFolderFileWithNullMetadata_IsStillClaimed()
    {
        var bookDirectory = Path.Join(
            FileService.GetTempDirectory("scan-service-null-metadata"),
            "Expected Title [B012345678]");
        var candidate = Path.Join(bookDirectory, "track01.m4b");
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .ReturnsAsync((AudioMetadata?)null);
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
        Directory.CreateDirectory(bookDirectory);
        await File.WriteAllTextAsync(candidate, "audio");
        await _applicationSettingsRepository.SaveAsync(
            new ApplicationSettingsBuilder()
                .WithOutputPath(FileService.GetTempPath())
                .Build());
        var audiobookToAdd = new AudiobookBuilder()
            .WithTitle("Expected Title")
            .WithAuthor("Expected Author")
            .Build();
        audiobookToAdd.Asin = "B012345678";
        var audiobook = await _audiobookRepository.AddAsync(audiobookToAdd);

        var result = await _provider
            .GetRequiredService<IAudiobookScanService>()
            .ScanAsync(await AuthorizedCommandAsync(audiobook.Id, bookDirectory));

        Assert.Equal(candidate, Assert.Single(result.AttributedFiles));
        Assert.Equal(1, result.CreatedCount);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataContradictsPath");
    }

    [LinuxFact]
    public async Task ScanAsync_IdentifierFolderFileMetadataProbeThrows_ClaimsFailOpenWithDiagnostic()
    {
        var bookDirectory = Path.Join(
            FileService.GetTempDirectory("scan-service-probe-throws"),
            "Expected Title [B012345678]");
        var candidate = Path.Join(bookDirectory, "track01.m4b");
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .ThrowsAsync(new IOException("Simulated unreadable file."));
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
        Directory.CreateDirectory(bookDirectory);
        await File.WriteAllTextAsync(candidate, "audio");
        await _applicationSettingsRepository.SaveAsync(
            new ApplicationSettingsBuilder()
                .WithOutputPath(FileService.GetTempPath())
                .Build());
        var audiobookToAdd = new AudiobookBuilder()
            .WithTitle("Expected Title")
            .WithAuthor("Expected Author")
            .Build();
        audiobookToAdd.Asin = "B012345678";
        var audiobook = await _audiobookRepository.AddAsync(audiobookToAdd);

        var result = await _provider
            .GetRequiredService<IAudiobookScanService>()
            .ScanAsync(await AuthorizedCommandAsync(audiobook.Id, bookDirectory));

        Assert.Equal(candidate, Assert.Single(result.AttributedFiles));
        Assert.Equal(1, result.CreatedCount);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataContradictsPath");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataUnavailable"
            && diagnostic.Path == candidate);
    }

    [LinuxFact]
    public async Task ScanAsync_TrackedFileWithContradictingTags_IsNotReprobedOrDeclined()
    {
        // An already-tracked file is never a content-verification candidate at all: the
        // decline exists to stop a wrong file being claimed, and this file is already
        // durably owned, so there is nothing left to protect by reading it again.
        var bookDirectory = Path.Join(
            FileService.GetTempDirectory("scan-service-tracked-contradicting-tags"),
            "Expected Title [B012345678]");
        var trackedFile = Path.Join(bookDirectory, "tracked.m4b");
        Directory.CreateDirectory(bookDirectory);
        await File.WriteAllTextAsync(trackedFile, "audio");
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .ReturnsAsync(new AudioMetadata
            {
                Title = "Unrelated Title",
                Album = "Unrelated Title",
                Artist = "Unrelated Author",
                AlbumArtist = "Unrelated Author",
                Duration = TimeSpan.FromSeconds(1),
                Format = "m4b"
            });
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
        await _applicationSettingsRepository.SaveAsync(
            new ApplicationSettingsBuilder()
                .WithOutputPath(FileService.GetTempPath())
                .Build());
        var audiobookToAdd = new AudiobookBuilder()
            .WithTitle("Expected Title")
            .WithAuthor("Expected Author")
            .Build();
        audiobookToAdd.Asin = "B012345678";
        var audiobook = await _audiobookRepository.AddAsync(audiobookToAdd);
        // Give it real duration/format/sample-rate so the unrelated background
        // MetadataRescanService does not also pick it up as "missing metadata" and
        // probe it independently of the scan under test.
        var trackedFileRecord = new AudiobookFileBuilder()
            .WithAudiobook(audiobook)
            .WithPath(trackedFile)
            .WithFormat("m4b")
            .WithSampleRate(44100)
            .Build();
        trackedFileRecord.DurationSeconds = 1;
        await _audiobookFileRepository.AddAsync(trackedFileRecord);

        var result = await _provider
            .GetRequiredService<IAudiobookScanService>()
            .ScanAsync(await AuthorizedCommandAsync(audiobook.Id, bookDirectory));

        Assert.Contains(trackedFile, result.AttributedFiles);
        Assert.Empty(result.RemovedFiles);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataContradictsPath");
        // Exactly one probe, not zero: this tracked row was seeded without a physical
        // object identity, so the pre-existing (and unrelated) backfill reconciliation in
        // ReconcileMissingFilesAsync/RefreshPhysicalGenerationAsync legitimately reads it
        // once to enroll one. What this proves is the absence of a SECOND probe: the new
        // content-verification pass's own candidate filter excludes every owned path (see
        // IsOwnedOrLegacy), so it never also reads this file. Two calls here would mean
        // that exclusion regressed.
        metadata.Verify(
            service => service.ExtractFileMetadataAsync(
                It.Is<MetadataFileSource>(source => source.PublicPath == trackedFile)),
            Times.Once);
    }

    [LinuxFact]
    public async Task ScanAsync_MixedFolderUntaggedAndWrongBookFile_ClaimsOnlyTheUntaggedOne()
    {
        var bookDirectory = Path.Join(
            FileService.GetTempDirectory("scan-service-mixed-folder"),
            "Expected Title [B012345678]");
        var untaggedFile = Path.Join(bookDirectory, "aa-untagged.m4b");
        var wrongBookFile = Path.Join(bookDirectory, "bb-borrowed.m4b");
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .Returns<MetadataFileSource>(fileSource => Task.FromResult<AudioMetadata?>(
                string.Equals(fileSource.PublicPath, untaggedFile, StringComparison.Ordinal)
                    ? new AudioMetadata
                    {
                        Title = "aa-untagged",
                        Duration = TimeSpan.FromSeconds(1),
                        Format = "m4b"
                    }
                    : new AudioMetadata
                    {
                        Title = "Unrelated Title",
                        Album = "Unrelated Title",
                        Artist = "Unrelated Author",
                        AlbumArtist = "Unrelated Author",
                        Duration = TimeSpan.FromSeconds(1),
                        Format = "m4b"
                    }));
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
        Directory.CreateDirectory(bookDirectory);
        await File.WriteAllTextAsync(untaggedFile, "audio");
        await File.WriteAllTextAsync(wrongBookFile, "audio");
        await _applicationSettingsRepository.SaveAsync(
            new ApplicationSettingsBuilder()
                .WithOutputPath(FileService.GetTempPath())
                .Build());
        var audiobookToAdd = new AudiobookBuilder()
            .WithTitle("Expected Title")
            .WithAuthor("Expected Author")
            .Build();
        audiobookToAdd.Asin = "B012345678";
        var audiobook = await _audiobookRepository.AddAsync(audiobookToAdd);

        var result = await _provider
            .GetRequiredService<IAudiobookScanService>()
            .ScanAsync(await AuthorizedCommandAsync(audiobook.Id, bookDirectory));

        Assert.Equal(untaggedFile, Assert.Single(result.AttributedFiles));
        Assert.Equal(1, result.CreatedCount);
        Assert.True(result.HasDurableAttributedOwnership);
        var contradiction = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataContradictsPath");
        Assert.Equal(wrongBookFile, contradiction.Path);
        Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));
    }

    [LinuxFact]
    public async Task ScanAsync_DeclinedBookBoundaryFile_IsNotReprobedByEnrichment()
    {
        // No stable-identifier boundary here, so without the ContentDeclinedCandidates
        // exclusion in EnrichWithMetadataAsync's filter, this exact path would otherwise
        // pass CanClaimNewPath and get probed a second time after being declined.
        var authorDirectory = Path.Join(
            FileService.GetTempDirectory("scan-service-declined-not-reprobed"),
            "Expected Author");
        var bookDirectory = Path.Join(authorDirectory, "Expected Title");
        Directory.CreateDirectory(bookDirectory);
        var candidate = Path.Join(bookDirectory, "borrowed.m4b");
        var probeCount = 0;
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .Callback(() => probeCount++)
            .ReturnsAsync(new AudioMetadata
            {
                Title = "Unrelated Title",
                Album = "Unrelated Title",
                Artist = "Unrelated Author",
                AlbumArtist = "Unrelated Author",
                Duration = TimeSpan.FromSeconds(1),
                Format = "m4b"
            });
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
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

        var result = await _provider
            .GetRequiredService<IAudiobookScanService>()
            .ScanAsync(await AuthorizedCommandAsync(audiobook.Id, bookDirectory));

        Assert.Empty(result.AttributedFiles);
        Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataContradictsPath");
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataDeclined");
        Assert.Equal(1, probeCount);
    }

    [LinuxFact]
    public async Task ScanAsync_PinnedPathOnly_WrongBookFileDeclinedUntaggedFileClaimed()
    {
        // The new content-verification pass is never gated on durable generation proof:
        // a decline never claims anything, so it is safe even on storage that cannot
        // prove file identity. Both halves of that claim are exercised here in one scan.
        var bookDirectory = Path.Join(
            FileService.GetTempDirectory("scan-service-limited-storage-wrong-book"),
            "Expected Title [B012345678]");
        var untaggedFile = Path.Join(bookDirectory, "aa-untagged.m4b");
        var wrongBookFile = Path.Join(bookDirectory, "bb-borrowed.m4b");
        Directory.CreateDirectory(bookDirectory);
        await File.WriteAllTextAsync(untaggedFile, "audio");
        await File.WriteAllTextAsync(wrongBookFile, "audio");
        var semantics = FileSystemPathSemantics.CurrentHostDefault;
        var pathIdentity = new PathIdentitySnapshot(
            semantics.Syntax,
            semantics.CaseSensitivity,
            semantics.CaseSensitivity == FileSystemCaseSensitivity.Sensitive
                ? FileSystemCaseSensitivityMode.Sensitive
                : FileSystemCaseSensitivityMode.Insensitive,
            bookDirectory);
        var physicalIdentity = ScanPathPhysicalIdentity.PinnedPathOnly();
        var authorization = new Mock<IScanPathAuthorizationService>(MockBehavior.Strict);
        authorization
            .Setup(service => service.AuthorizeAsync(
                bookDirectory,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScanPathAuthorizationResult.Authorized(
                bookDirectory,
                pathIdentity,
                physicalIdentity));
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .Returns<MetadataFileSource>(fileSource => Task.FromResult<AudioMetadata?>(
                string.Equals(fileSource.PublicPath, untaggedFile, StringComparison.Ordinal)
                    ? new AudioMetadata
                    {
                        Title = "aa-untagged",
                        Duration = TimeSpan.FromSeconds(1),
                        Format = "m4b"
                    }
                    : new AudioMetadata
                    {
                        Title = "Unrelated Title",
                        Album = "Unrelated Title",
                        Artist = "Unrelated Author",
                        AlbumArtist = "Unrelated Author",
                        Duration = TimeSpan.FromSeconds(1),
                        Format = "m4b"
                    }));
        _services.AddSingleton(authorization.Object);
        _services.AddSingleton(metadata.Object);
        Init();
        await _applicationSettingsRepository.SaveAsync(
            new ApplicationSettingsBuilder()
                .WithOutputPath(FileService.GetTempPath())
                .Build());
        var audiobookToAdd = new AudiobookBuilder()
            .WithTitle("Expected Title")
            .WithAuthor("Expected Author")
            .Build();
        audiobookToAdd.Asin = "B012345678";
        var audiobook = await _audiobookRepository.AddAsync(audiobookToAdd);

        var result = await _provider.GetRequiredService<IAudiobookScanService>()
            .ScanAsync(new AudiobookScanCommand(
                audiobook.Id,
                bookDirectory,
                pathIdentity,
                physicalIdentity));

        Assert.Equal(untaggedFile, Assert.Single(result.AttributedFiles));
        Assert.Equal(1, result.CreatedCount);
        var contradiction = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataContradictsPath");
        Assert.Equal(wrongBookFile, contradiction.Path);
        authorization.VerifyAll();
    }

    [LinuxFact]
    public async Task ScanAsync_IdentifierFolderHoldsWrongBookFileAndForeignOwnedFile_CountsOnlyTheWrongBookFile()
    {
        var root = FileService.GetTempDirectory("scan-service-wrong-book-and-foreign");
        var identifierDirectory = Path.Join(root, "Expected Title [B012345678]");
        Directory.CreateDirectory(identifierDirectory);
        var foreignOwnedFile = Path.Join(identifierDirectory, "Owned By Other.m4b");
        var wrongBookFile = Path.Join(identifierDirectory, "borrowed.m4b");
        await File.WriteAllTextAsync(foreignOwnedFile, "audio");
        var probedPublicPaths = new List<string>();
        var metadata = new Mock<IMetadataService>(MockBehavior.Strict);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .Returns<MetadataFileSource>(fileSource =>
            {
                probedPublicPaths.Add(fileSource.PublicPath);
                return Task.FromResult<AudioMetadata?>(
                    string.Equals(
                        fileSource.PublicPath,
                        foreignOwnedFile,
                        StringComparison.Ordinal)
                        ? new AudioMetadata
                        {
                            Title = "Owned By Other",
                            Duration = TimeSpan.FromSeconds(1),
                            Format = "m4b"
                        }
                        : new AudioMetadata
                        {
                            Title = "Unrelated Title",
                            Album = "Unrelated Title",
                            Artist = "Unrelated Author",
                            AlbumArtist = "Unrelated Author",
                            Duration = TimeSpan.FromSeconds(1),
                            Format = "m4b"
                        });
            });
        Init(services => services.WithSingleton<IMetadataService>(metadata.Object));
        await _applicationSettingsRepository.SaveAsync(
            new ApplicationSettingsBuilder()
                .WithOutputPath(FileService.GetTempPath())
                .Build());
        var other = await _audiobookRepository.AddAsync(
            new AudiobookBuilder()
                .WithTitle("Owned By Other")
                .Build());
        var scanService = _provider.GetRequiredService<IAudiobookScanService>();

        // Control: the owner claims its file through the exact-filename match before
        // the wrong-book file exists, so the later scan sees a genuinely foreign owner.
        var ownerResult = await scanService.ScanAsync(await AuthorizedCommandAsync(
            other.Id,
            identifierDirectory));
        Assert.Equal(foreignOwnedFile, Assert.Single(ownerResult.AttributedFiles));
        Assert.Equal(1, ownerResult.CreatedCount);
        probedPublicPaths.Clear();

        await File.WriteAllTextAsync(wrongBookFile, "audio");
        var audiobookToAdd = new AudiobookBuilder()
            .WithTitle("Expected Title")
            .WithAuthor("Expected Author")
            .Build();
        audiobookToAdd.Asin = "B012345678";
        var audiobook = await _audiobookRepository.AddAsync(audiobookToAdd);

        var result = await scanService.ScanAsync(await AuthorizedCommandAsync(
            audiobook.Id,
            identifierDirectory));

        Assert.Empty(result.AttributedFiles);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.DiscoveredCandidateCount);
        Assert.False(result.HasDurableAttributedOwnership);
        var contradiction = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataContradictsPath");
        Assert.Equal(wrongBookFile, contradiction.Path);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MetadataDeclined");
        // The foreign-owned file is never a content-verification candidate (it never
        // reached discovery.AttributedFiles), but the existing enrichment loop still
        // probes it once for conflict-detection purposes: two probes total.
        Assert.Equal(2, probedPublicPaths.Count);
        Assert.Contains(wrongBookFile, probedPublicPaths);
        Assert.Contains(foreignOwnedFile, probedPublicPaths);
        Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(other.Id));
        Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));
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
