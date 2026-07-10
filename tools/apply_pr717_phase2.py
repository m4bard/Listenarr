from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    (ROOT / path).write_text(content, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    content = read(path)
    count = content.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one match, found {count}")
    write(path, content.replace(old, new, 1))


replace_once(
    "listenarr.infrastructure/FileSystem/FileSystemSafety.cs",
    """    public static void DeleteEmptyDirectories(string rootPath)
""",
    """    public static bool TryDeleteEmptyDirectory(
        string directoryPath,
        IEnumerable<string?> allowedRoots,
        out string reason)
    {
        reason = string.Empty;
        try
        {
            if (!TryValidateMutationTarget(
                    directoryPath,
                    allowedRoots,
                    out var normalizedDirectory,
                    out reason))
            {
                return false;
            }

            if (!Directory.Exists(normalizedDirectory))
            {
                return true;
            }

            if ((File.GetAttributes(normalizedDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                reason = "Directory deletion was blocked because the target is a symbolic link or reparse point.";
                return false;
            }

            if (Directory.EnumerateFileSystemEntries(normalizedDirectory).Any())
            {
                reason = "Directory deletion was blocked because the target is not empty.";
                return false;
            }

            Directory.Delete(normalizedDirectory, recursive: false);
            return true;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            reason = $"Directory deletion failed safely: {exception.GetType().Name}.";
            return false;
        }
    }

    public static void DeleteEmptyDirectories(string rootPath)
""",
)

service_path = "listenarr.infrastructure/Library/Moving/AudiobookFilesystemDeleteService.cs"
replace_once(
    service_path,
    """                var contentsDeleted = TryDeleteFolderContents(deleteTarget.FolderPath, result);
""",
    """                var contentsDeleted = TryDeleteFolderContents(deleteTarget, result);
""",
)
replace_once(
    service_path,
    """            public required IReadOnlyCollection<string> ProtectedRoots { get; init; }
            public required FileSystemPathSemantics Semantics { get; init; }
""",
    """            public required IReadOnlyCollection<string> ProtectedRoots { get; init; }
            public required IReadOnlyCollection<string> AllowedMutationRoots { get; init; }
            public required FileSystemPathSemantics Semantics { get; init; }
""",
)
replace_once(
    service_path,
    '''        private bool TryDeleteFolderContents(string folderPath, AudiobookFilesystemDeleteResult result)
        {
            if (!Directory.Exists(folderPath))
            {
                return true;
            }

            if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
                folderPath,
                out var files,
                out var directories,
                out var reason))
            {
                result.Warnings.Add(
                    "Refused to recursively delete the audiobook folder because it contains a symbolic link or reparse point.");
                _logger.LogWarning(
                    "Blocked recursive audiobook delete for {FolderPath}: {Reason}",
                    LogRedaction.SanitizeFilePath(folderPath),
                    LogRedaction.SanitizeText(reason));
                return false;
            }

            foreach (var filePath in files)
            {
                TryDeleteFile(filePath, result, [folderPath]);
            }

            foreach (var directoryPath in directories.OrderByDescending(path => path.Length))
            {
                try
                {
                    if (!Directory.Exists(directoryPath)
                        || (File.GetAttributes(directoryPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
                    {
                        Directory.Delete(directoryPath, recursive: false);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Failed to remove nested audiobook directory {FolderPath}", LogRedaction.SanitizeFilePath(directoryPath));
                }
            }

            return true;
        }
''',
    '''        private bool TryDeleteFolderContents(
            DeleteFolderTarget deleteTarget,
            AudiobookFilesystemDeleteResult result)
        {
            var folderPath = deleteTarget.FolderPath;
            if (!Directory.Exists(folderPath))
            {
                return true;
            }

            if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
                folderPath,
                out var files,
                out var directories,
                out var reason))
            {
                result.Warnings.Add(
                    "Refused to recursively delete the audiobook folder because it contains a symbolic link or reparse point.");
                _logger.LogWarning(
                    "Blocked recursive audiobook delete for {FolderPath}: {Reason}",
                    LogRedaction.SanitizeFilePath(folderPath),
                    LogRedaction.SanitizeText(reason));
                return false;
            }

            foreach (var filePath in files)
            {
                TryDeleteFile(filePath, result, deleteTarget.AllowedMutationRoots);
            }

            foreach (var directoryPath in directories.OrderByDescending(path => path.Length))
            {
                if (!FileSystemSafety.TryDeleteEmptyDirectory(
                        directoryPath,
                        deleteTarget.AllowedMutationRoots,
                        out var directoryReason))
                {
                    _logger.LogDebug(
                        "Skipped nested audiobook directory delete for {FolderPath}: {Reason}",
                        LogRedaction.SanitizeFilePath(directoryPath),
                        LogRedaction.SanitizeText(directoryReason));
                }
            }

            return true;
        }
''',
)

folders_path = "listenarr.infrastructure/Library/Moving/AudiobookFilesystemDeleteService.Folders.cs"
replace_once(
    folders_path,
    """            return new DeleteFolderTarget
            {
                FolderPath = folderPath,
                ProtectedRoots = protectedRoots,
                Semantics = semantics
            };
""",
    """            var allowedMutationRoots = protectedRoots
                .Where(root => IsSamePathOrWithin(folderPath, root, semantics))
                .ToList();
            if (allowedMutationRoots.Count == 0)
            {
                allowedMutationRoots.Add(folderPath);
            }

            return new DeleteFolderTarget
            {
                FolderPath = folderPath,
                ProtectedRoots = protectedRoots,
                AllowedMutationRoots = allowedMutationRoots,
                Semantics = semantics
            };
""",
)
replace_once(
    folders_path,
    '''            try
            {
                // Contents were enumerated and deleted individually above. Refuse to
                // remove anything that appeared concurrently after that snapshot.
                Directory.Delete(deleteTarget.FolderPath, recursive: false);
                result.DeletedFolder = true;
                _logger.LogInformation("Deleted audiobook folder {FolderPath}", LogRedaction.SanitizeFilePath(deleteTarget.FolderPath));
                await TryDeleteEmptyAuthorFolderAsync(
                    audiobook,
                    deleteTarget.FolderPath,
                    deleteTarget.ProtectedRoots,
                    deleteTarget.Semantics,
                    result);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Warnings.Add("Failed to delete the audiobook folder.");
                _logger.LogWarning(ex, "Failed to delete audiobook folder {FolderPath}", LogRedaction.SanitizeFilePath(deleteTarget.FolderPath));
            }
''',
    '''            // Contents were enumerated and deleted individually above. Revalidate every
            // existing path component immediately before removing the now-empty folder.
            if (!FileSystemSafety.TryDeleteEmptyDirectory(
                    deleteTarget.FolderPath,
                    deleteTarget.AllowedMutationRoots,
                    out var reason))
            {
                result.Warnings.Add("Failed to delete the audiobook folder.");
                _logger.LogWarning(
                    "Failed to safely delete audiobook folder {FolderPath}: {Reason}",
                    LogRedaction.SanitizeFilePath(deleteTarget.FolderPath),
                    LogRedaction.SanitizeText(reason));
                return;
            }

            result.DeletedFolder = true;
            _logger.LogInformation("Deleted audiobook folder {FolderPath}", LogRedaction.SanitizeFilePath(deleteTarget.FolderPath));
            await TryDeleteEmptyAuthorFolderAsync(
                audiobook,
                deleteTarget.FolderPath,
                deleteTarget.ProtectedRoots,
                deleteTarget.Semantics,
                result);
''',
)
replace_once(
    folders_path,
    '''            try
            {
                Directory.Delete(parentFolder, recursive: false);
                result.DeletedParentFolder = true;
                _logger.LogInformation("Deleted empty parent author folder {FolderPath}", LogRedaction.SanitizeFilePath(parentFolder));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Warnings.Add("Failed to delete the empty author folder.");
                _logger.LogWarning(ex, "Failed to delete empty parent author folder {FolderPath}", LogRedaction.SanitizeFilePath(parentFolder));
            }
''',
    '''            var allowedMutationRoots = protectedRoots
                .Where(root => IsSamePathOrWithin(parentFolder, root, semantics))
                .ToList();
            if (allowedMutationRoots.Count == 0)
            {
                allowedMutationRoots.Add(parentFolder);
            }

            if (!FileSystemSafety.TryDeleteEmptyDirectory(
                    parentFolder,
                    allowedMutationRoots,
                    out var reason))
            {
                result.Warnings.Add("Failed to delete the empty author folder.");
                _logger.LogWarning(
                    "Failed to safely delete empty parent author folder {FolderPath}: {Reason}",
                    LogRedaction.SanitizeFilePath(parentFolder),
                    LogRedaction.SanitizeText(reason));
                return;
            }

            result.DeletedParentFolder = true;
            _logger.LogInformation("Deleted empty parent author folder {FolderPath}", LogRedaction.SanitizeFilePath(parentFolder));
''',
)

link_test_path = "tests/Features/Api/Features/Library/LibraryController_DeleteLinkSafetyTests.cs"
replace_once(
    link_test_path,
    """    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
""",
    '''    [Fact]
    public async Task FilesystemDelete_LinkedFileDoesNotDeleteExternalFile()
    {
        var tempRoot = FileService.GetTempDirectory("listenarr-delete-file-link-root");
        var bookFolder = Path.Join(tempRoot, "Book");
        var externalFolder = FileService.GetTempDirectory("listenarr-delete-file-link-external");
        var localFile = Path.Join(bookFolder, "book.m4b");
        var externalFile = Path.Join(externalFolder, "external.txt");
        var linkedFile = Path.Join(bookFolder, "linked.txt");
        Directory.CreateDirectory(bookFolder);
        await File.WriteAllTextAsync(localFile, "audio");
        await File.WriteAllTextAsync(externalFile, "external");

        try
        {
            File.CreateSymbolicLink(linkedFile, externalFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Linked File Book")
            .WithBasePath(bookFolder)
            .WithFilePath(localFile)
            .Build());
        await _audiobookFileRepository.AddAsync(new AudiobookFileBuilder()
            .WithAudiobook(audiobook)
            .WithPath(localFile)
            .Build());

        var service = _provider.GetRequiredService<IAudiobookFilesystemDeleteService>();
        var result = await service.DeleteAsync(audiobook, deleteFolder: true);

        Assert.True(File.Exists(externalFile));
        Assert.True(File.Exists(localFile));
        Assert.True(File.Exists(linkedFile));
        Assert.False(result.DeletedFolder);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("symbolic link", StringComparison.OrdinalIgnoreCase)
            || warning.Contains("reparse point", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
''',
)
