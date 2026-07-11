using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private sealed record ValidatedSourceEntry(
        string FullPath,
        string RelativePath,
        bool IsDirectory,
        DateTime LastWriteTimeUtc);

    private static IReadOnlyList<ValidatedSourceEntry> ValidateSourceTreeForMove(
        string source,
        string target,
        bool targetInsideSource,
        FileSystemPathSemantics sourceSemantics,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source))
        {
            throw new MoveNeedsAttentionException("The move source directory does not exist.");
        }

        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException("Move sources cannot be symlinks or reparse points.");
        }

        var entries = new List<ValidatedSourceEntry>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(source);
        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (targetInsideSource && IsSameOrInside(entry, target, sourceSemantics))
                {
                    continue;
                }

                var entryName = Path.GetFileName(entry);
                if (IsReservedMoveArtifactName(entryName))
                {
                    throw new MoveNeedsAttentionException(
                        $"Move source contains a reserved Listenarr recovery artifact that must be resolved before moving: {Path.GetRelativePath(source, entry)}");
                }

                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new MoveNeedsAttentionException(
                        $"Move entry '{Path.GetRelativePath(source, entry)}' is a symlink or reparse point.");
                }

                if (!FileSystemPathIdentity.TryGetRelativePathWithinBase(
                        source,
                        entry,
                        sourceSemantics,
                        out var relativePath))
                {
                    throw new MoveNeedsAttentionException("A source entry escaped the source root.");
                }

                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                entries.Add(new ValidatedSourceEntry(
                    entry,
                    relativePath,
                    isDirectory,
                    isDirectory
                        ? Directory.GetLastWriteTimeUtc(entry)
                        : File.GetLastWriteTimeUtc(entry)));
                if (isDirectory)
                {
                    pendingDirectories.Push(entry);
                }
            }
        }

        return entries;
    }

    private static bool IsReservedMoveArtifactName(string name) =>
        name.StartsWith(".listenarr-move-", StringComparison.Ordinal)
        || name.StartsWith(".listenarr-quarantine-", StringComparison.Ordinal)
        || name.StartsWith(".listenarr-temporary-directory-", StringComparison.Ordinal)
        || string.Equals(name, ".listenarr-temp-owner.json", StringComparison.Ordinal)
        || string.Equals(name, ".listenarr-quarantine-owner.json", StringComparison.Ordinal)
        || name.Contains(".listenarr-", StringComparison.Ordinal)
            && name.EndsWith(".partial", StringComparison.Ordinal);

    private static async Task<List<MoveJobEntry>> BuildManifestAsync(
        Guid jobId,
        IReadOnlyList<ValidatedSourceEntry> validatedEntries,
        CancellationToken cancellationToken)
    {
        var manifest = new List<MoveJobEntry>(validatedEntries.Count);
        foreach (var entry in validatedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry.FullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0
                || ((attributes & FileAttributes.Directory) != 0) != entry.IsDirectory)
            {
                throw new MoveNeedsAttentionException(
                    $"Move entry changed type or became linked after validation: {entry.RelativePath}");
            }

            if (entry.IsDirectory)
            {
                manifest.Add(new MoveJobEntry
                {
                    MoveJobId = jobId,
                    RelativePath = entry.RelativePath,
                    EntryType = MoveJobEntryType.Directory,
                    LastWriteTimeUtc = entry.LastWriteTimeUtc
                });
                continue;
            }

            var fileInfo = new FileInfo(entry.FullPath);
            manifest.Add(new MoveJobEntry
            {
                MoveJobId = jobId,
                RelativePath = entry.RelativePath,
                EntryType = MoveJobEntryType.File,
                Length = fileInfo.Length,
                LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                Sha256 = await ComputeSha256Async(entry.FullPath, cancellationToken)
            });
        }

        return manifest;
    }
}
