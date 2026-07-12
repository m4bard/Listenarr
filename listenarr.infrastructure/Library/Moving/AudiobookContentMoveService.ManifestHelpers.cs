using System.Security.Cryptography;
using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private static void ResolveCleanupPaths(
        string source,
        string quarantineRoot,
        string relativePath,
        FileSystemPathSemantics semantics,
        out string sourceFile,
        out string quarantineFile)
    {
        if (!FileSystemPathIdentity.TryResolveRelativePathWithinBase(
            source,
            relativePath,
            semantics,
            out sourceFile)
            || !FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                quarantineRoot,
                relativePath,
                semantics,
                out quarantineFile))
        {
            throw new MoveNeedsAttentionException(
                "A manifest entry escaped its cleanup boundary.");
        }
    }

    private static bool ManifestMatches(
        IReadOnlyCollection<MoveJobEntry> expected,
        IReadOnlyCollection<MoveJobEntry> current,
        FileSystemPathSemantics semantics)
    {
        if (expected.Count != current.Count)
        {
            return false;
        }

        var currentByPath = current.ToDictionary(
            entry => entry.RelativePath,
            semantics.Comparer);
        return expected.All(entry =>
            currentByPath.TryGetValue(entry.RelativePath, out var currentEntry)
            && currentEntry.EntryType == entry.EntryType
            && currentEntry.Length == entry.Length
            && string.Equals(
                currentEntry.Sha256,
                entry.Sha256,
                StringComparison.Ordinal));
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
