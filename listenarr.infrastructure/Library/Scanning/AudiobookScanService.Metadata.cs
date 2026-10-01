using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Scanning;

internal sealed partial class AudiobookScanService
{
    private async Task<ScanDiscoveryResult> EnrichWithMetadataAsync(
        AudiobookScanCommand command,
        PinnedScanAuthority pinnedAuthority,
        ScanDiscoveryResult discovery,
        Audiobook audiobook,
        string scanRoot,
        FileSystemPathSemantics semantics,
        IEnumerable<string> ownedPaths,
        ICollection<AudiobookScanDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!command.ScanPhysicalIdentity.HasDurableGenerationProof)
        {
            diagnostics.Add(new AudiobookScanDiagnostic(
                "MetadataEnrichmentSkippedLimitedStorage",
                scanRoot,
                "Embedded-metadata attribution was skipped because this storage does not expose durable file-generation identity."));
            return discovery;
        }

        var attributed = new HashSet<string>(
            discovery.AttributedFiles,
            semantics.Comparer);
        var boundaries = new Dictionary<string, string>(
            discovery.ProvenBookBoundaries,
            semantics.Comparer);
        var issues = discovery.Issues.ToList();
        var metadataMatches = new List<string>();
        var owned = CanonicalOwnedPaths(ownedPaths, semantics);

        if (discovery.HasStableIdentifierBoundaryConflict)
        {
            return discovery with { Issues = issues };
        }

        // Files another audiobook already owns are skipped before extraction: discovery
        // already refuses to attribute them, a match could only fail at claim time, and
        // a decline would be noise about a file that was never this book's to take.
        foreach (var candidate in discovery.Candidates.Where(path =>
            !attributed.Contains(path)
            && IsClaimableCandidate(path, discovery, owned, semantics)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ValidateDiscoveredPathParent(
                    command,
                    pinnedAuthority,
                    discovery,
                    candidate);
                using var pinnedMetadataFile = OpenPinnedMetadataFile(
                    command,
                    pinnedAuthority,
                    discovery,
                    candidate);
                // The lease deliberately separates stable byte access from public media
                // identity, and the single-path overload collapses the two. On Linux the
                // metadata path is a /proc descriptor link with no extension, so collapsing
                // it makes the probe's audio-extension guard reject the candidate before
                // ffprobe runs.
                var metadata = await metadataService.ExtractFileMetadataAsync(
                    new MetadataFileSource(
                        pinnedMetadataFile.MetadataPath,
                        candidate));
                if (metadata != null
                    && ScanFileDiscovery.MetadataMatchesAudiobook(metadata, audiobook))
                {
                    metadataMatches.Add(candidate);
                }
                else if (metadata != null)
                {
                    // A diagnostic, not a ScanDiscoveryIssue: issues feed completeness
                    // and attribution-conflict decisions, and a decline must not change
                    // what the scan does, only make the skipped file visible. This also
                    // covers the filename fallback MetadataService returns when ffprobe
                    // fails. Name the candidate, never the pinned descriptor path.
                    logger.LogInformation(
                        "Embedded metadata for scan candidate {Path} did not match audiobook {AudiobookId}; the file was not attributed",
                        LogRedaction.SanitizeFilePath(candidate),
                        audiobook.Id);
                    diagnostics.Add(new AudiobookScanDiagnostic(
                        "MetadataDeclined",
                        candidate,
                        "The file's metadata did not match this audiobook, so the file was not added."));
                }
            }
            catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
            {
                logger.LogWarning(
                    exception,
                    "Metadata enrichment failed for scan candidate {Path}",
                    LogRedaction.SanitizeFilePath(candidate));
                issues.Add(new ScanDiscoveryIssue(
                    ScanDiscoveryIssueKind.MetadataUnavailable,
                    candidate,
                    "Embedded metadata could not be read safely."));
            }
        }

        if (metadataMatches.Count == 0)
        {
            return discovery with { Issues = issues };
        }

        var metadataBoundary = discovery.SelectedStableIdentifierBoundary;
        if (metadataBoundary == null)
        {
            var strongPaths = ownedPaths
                .Concat(metadataMatches)
                .Distinct(semantics.Comparer)
                .ToList();
            metadataBoundary = CalculateMetadataBoundary(
                strongPaths,
                scanRoot,
                semantics);
        }
        if (metadataBoundary == null)
        {
            issues.Add(new ScanDiscoveryIssue(
                ScanDiscoveryIssueKind.AttributionConflict,
                scanRoot,
                "Embedded metadata matched files in multiple unrelated book directories."));
            diagnostics.Add(new AudiobookScanDiagnostic(
                "MetadataAttributionConflict",
                scanRoot,
                "Embedded metadata matched multiple unrelated book directories; no ambiguous files were claimed."));
            return discovery with { Issues = issues };
        }

        foreach (var match in metadataMatches)
        {
            attributed.Add(match);
            boundaries[match] = metadataBoundary;
        }

        return discovery with
        {
            AttributedFiles = attributed
                .OrderBy(path => path, semantics.Comparer)
                .ToList(),
            ProvenBookBoundaries = boundaries,
            Issues = issues
        };
    }

    // Counts the audio files this book could actually have claimed: not owned by
    // another audiobook, and not outside a selected stable-identifier boundary. A
    // shared root full of other books' files must not read as "files found here".
    private static int CountClaimableCandidates(
        ScanDiscoveryResult discovery,
        IEnumerable<string> ownedPaths,
        FileSystemPathSemantics semantics)
    {
        var owned = CanonicalOwnedPaths(ownedPaths, semantics);
        return discovery.Candidates.Count(path =>
            IsClaimableCandidate(path, discovery, owned, semantics));
    }

    private static bool IsClaimableCandidate(
        string path,
        ScanDiscoveryResult discovery,
        IReadOnlySet<string> ownedCanonicalPaths,
        FileSystemPathSemantics semantics) =>
        !discovery.ForeignOwnedCandidates.Contains(path)
        && ScanFileDiscovery.CanClaimNewPath(
            path,
            discovery.SelectedStableIdentifierBoundary,
            ownedCanonicalPaths,
            semantics);

    private static HashSet<string> CanonicalOwnedPaths(
        IEnumerable<string> ownedPaths,
        FileSystemPathSemantics semantics) =>
        new(
            ownedPaths.Select(path => FileSystemPathIdentity.Canonicalize(
                path,
                semantics.Syntax)),
            semantics.Comparer);

    private static string? CalculateMetadataBoundary(
        IReadOnlyCollection<string> paths,
        string scanRoot,
        FileSystemPathSemantics semantics)
    {
        var directories = paths
            .Select(path => FileSystemPathIdentity.Canonicalize(
                Path.GetDirectoryName(path) ?? path,
                semantics.Syntax))
            .Distinct(semantics.Comparer)
            .ToList();
        if (directories.Count == 0)
        {
            return null;
        }

        var common = directories.Count == 1
            ? directories[0]
            : FileUtils.GetCommonPathForDirectories(directories, semantics);
        if (string.IsNullOrWhiteSpace(common)
            || !FileSystemPathIdentity.IsSameOrInside(
                common,
                scanRoot,
                semantics))
        {
            return null;
        }

        var relativeFirstSegments = new HashSet<string>(semantics.Comparer);
        var hasDirectFile = false;
        foreach (var directory in directories)
        {
            if (FileSystemPathIdentity.AreEquivalent(directory, common, semantics))
            {
                hasDirectFile = true;
                continue;
            }

            var relative = Path.GetRelativePath(common, directory);
            var firstSegment = relative.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstSegment))
            {
                relativeFirstSegments.Add(firstSegment);
            }
        }

        if (!hasDirectFile
            && relativeFirstSegments.Count > 1
            && relativeFirstSegments.Any(segment => !IsDiscDirectory(segment)))
        {
            return null;
        }

        if (FileSystemPathIdentity.AreEquivalent(common, scanRoot, semantics)
            && !hasDirectFile
            && relativeFirstSegments.Count > 1)
        {
            return null;
        }

        return common;
    }

    private static bool IsDiscDirectory(string segment)
    {
        var normalized = ScanFileDiscovery.NormalizeMetadataToken(segment)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        foreach (var prefix in new[] { "cd", "disc", "disk", "part" })
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal)
                && normalized[prefix.Length..].All(char.IsDigit)
                && normalized.Length > prefix.Length)
            {
                return true;
            }
        }

        return false;
    }
}
