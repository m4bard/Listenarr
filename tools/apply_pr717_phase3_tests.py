from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "tests/Features/Infrastructure/Library/Moving/AudiobookContentMoveServiceTests.cs"
content = PATH.read_text(encoding="utf-8")
marker = """        private async Task<AudiobookContentMoveRequest> CreateLeasedMoveRequestAsync(
"""
if content.count(marker) != 1:
    raise RuntimeError("move test insertion marker mismatch")
block = '''        [Fact]
        public async Task MoveContentsAsync_CopyStartedMarkerOwnedByAnotherJob_BlocksRecovery()
        {
            var source = FileService.GetTempDirectory("content-move-wrong-marker-src");
            var sourceFile = await FileService.GetFileAsync(source, "book.m4b", "verified audio");
            var target = FileService.GetTempDirectory("content-move-wrong-marker-dst");
            var jobId = Guid.NewGuid();
            await CreateLeasedMoveRequestAsync(source, target, jobId);
            await PersistFileManifestAsync(jobId, "book.m4b", sourceFile);
            await File.WriteAllTextAsync(
                Path.Join(target, $".listenarr-move-{jobId:N}.pending"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    Version = 1,
                    JobId = Guid.NewGuid(),
                    Source = source,
                    Target = target,
                    Stage = "copy-started"
                }));

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            var request = new AudiobookContentMoveRequest(
                source,
                target,
                jobId,
                true,
                FileSystemPathSemantics.CurrentHostDefault,
                FileSystemPathSemantics.CurrentHostDefault,
                LeaseToken(1));

            await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
                service.MoveContentsAsync(request, CancellationToken.None));

            Assert.True(File.Exists(sourceFile));
        }

        [Fact]
        public async Task MoveContentsAsync_CopyStartedWithUnknownDestinationFile_BlocksRecovery()
        {
            var source = FileService.GetTempDirectory("content-move-unowned-target-src");
            var sourceFile = await FileService.GetFileAsync(source, "book.m4b", "verified audio");
            var target = FileService.GetTempDirectory("content-move-unowned-target-dst");
            await FileService.GetFileAsync(target, "unrelated.txt", "not owned");
            var jobId = Guid.NewGuid();
            var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
            await PersistFileManifestAsync(jobId, "book.m4b", sourceFile);
            await File.WriteAllTextAsync(
                Path.Join(target, $".listenarr-move-{jobId:N}.pending"),
                "copy-started");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
                service.MoveContentsAsync(request, CancellationToken.None));

            Assert.Contains("unowned file", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(sourceFile));
            Assert.True(File.Exists(Path.Join(target, "unrelated.txt")));
        }

        [Fact]
        public async Task MoveContentsAsync_ValidOwnedPartial_PublishesFromPersistedManifest()
        {
            var source = FileService.GetTempDirectory("content-move-valid-partial-src");
            var sourceFile = await FileService.GetFileAsync(source, "book.m4b", "verified audio");
            var target = FileService.GetTempDirectory("content-move-valid-partial-dst");
            var jobId = Guid.NewGuid();
            var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
            await PersistFileManifestAsync(jobId, "book.m4b", sourceFile);
            var partial = Path.Join(target, $"book.m4b.listenarr-{jobId:N}.partial");
            await File.WriteAllTextAsync(partial, "verified audio");
            await File.WriteAllTextAsync(
                Path.Join(target, $".listenarr-move-{jobId:N}.pending"),
                "copy-started");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            await service.MoveContentsAsync(request, CancellationToken.None);

            Assert.False(File.Exists(partial));
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
            Assert.Equal("verified audio", await File.ReadAllTextAsync(Path.Join(target, "book.m4b")));
            Assert.False(Directory.Exists(source));
        }

        [Fact]
        public async Task MoveContentsAsync_InvalidOwnedPartial_IsReplacedFromManifestSource()
        {
            var source = FileService.GetTempDirectory("content-move-invalid-partial-src");
            var sourceFile = await FileService.GetFileAsync(source, "book.m4b", "verified audio");
            var target = FileService.GetTempDirectory("content-move-invalid-partial-dst");
            var jobId = Guid.NewGuid();
            var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
            await PersistFileManifestAsync(jobId, "book.m4b", sourceFile);
            var partial = Path.Join(target, $"book.m4b.listenarr-{jobId:N}.partial");
            await File.WriteAllTextAsync(partial, "invalid bytes");
            await File.WriteAllTextAsync(
                Path.Join(target, $".listenarr-move-{jobId:N}.pending"),
                "copy-started");

            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            await service.MoveContentsAsync(request, CancellationToken.None);

            Assert.False(File.Exists(partial));
            Assert.Equal("verified audio", await File.ReadAllTextAsync(Path.Join(target, "book.m4b")));
            Assert.False(Directory.Exists(source));
        }

        private async Task PersistFileManifestAsync(
            Guid jobId,
            string relativePath,
            string sourceFile)
        {
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    await File.ReadAllBytesAsync(sourceFile)));
            var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.MoveJobEntries.Add(new MoveJobEntry
            {
                MoveJobId = jobId,
                RelativePath = relativePath,
                EntryType = MoveJobEntryType.File,
                Length = new FileInfo(sourceFile).Length,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(sourceFile),
                Sha256 = hash
            });
            await db.SaveChangesAsync();
        }

'''
PATH.write_text(content.replace(marker, block + marker, 1), encoding="utf-8", newline="\n")
