using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Scanning;

internal sealed partial class AudiobookScanService
{
    // Discover() attributes files by folder/path-name evidence alone. This pass is the
    // content check that can override that evidence: it probes every attributed path
    // that isn't already-tracked (ExistingOwnership) or the audiobook's legacy FilePath,
    // and declines anything whose embedded tags unambiguously name a different book and
    // author (see ScanFileDiscovery.MetadataContradictsAudiobook). It runs before
    // EnrichWithMetadataAsync and, unlike that pass, is never gated on durable
    // generation proof: a decline never claims anything, so it is safe even on storage
    // that cannot prove file identity.
    private async Task<ScanDiscoveryResult> VerifyPathAttributedContentAsync(
        AudiobookScanCommand command,
        PinnedScanAuthority pinnedAuthority,
        ScanDiscoveryResult discovery,
        Audiobook audiobook,
        IEnumerable<string> ownedPaths,
        FileSystemPathSemantics semantics,
        ICollection<AudiobookScanDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var owned = CanonicalOwnedPaths(ownedPaths, semantics);
        var legacyPath = ResolveCanonicalLegacyPath(audiobook, semantics);

        var attributed = new HashSet<string>(
            discovery.AttributedFiles,
            semantics.Comparer);
        var boundaries = new Dictionary<string, string>(
            discovery.ProvenBookBoundaries,
            semantics.Comparer);
        var issues = discovery.Issues.ToList();
        var contentDeclined = new HashSet<string>(
            discovery.ContentDeclinedCandidates,
            semantics.Comparer);

        var candidates = discovery.AttributedFiles
            .Where(path => !IsOwnedOrLegacy(path, owned, legacyPath, semantics))
            .ToList();

        foreach (var candidate in candidates)
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
                var metadata = await metadataService.ExtractFileMetadataAsync(
                    new MetadataFileSource(
                        pinnedMetadataFile.MetadataPath,
                        candidate));
                if (metadata != null
                    && ScanFileDiscovery.MetadataContradictsAudiobook(
                        metadata,
                        audiobook,
                        candidate))
                {
                    attributed.Remove(candidate);
                    boundaries.Remove(candidate);
                    contentDeclined.Add(candidate);
                    logger.LogWarning(
                        "Embedded tags for {Path} name a different book than audiobook {AudiobookId}; the folder match was overridden and the file was not added",
                        LogRedaction.SanitizeFilePath(candidate),
                        audiobook.Id);
                    diagnostics.Add(new AudiobookScanDiagnostic(
                        "MetadataContradictsPath",
                        candidate,
                        "The file's embedded tags name a different book and author than this audiobook, so it was not added even though its folder matched."));
                }
            }
            catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
            {
                // Fail open: keep today's behavior (the folder match stands) rather than
                // let an unreadable file block a scan that would otherwise succeed.
                logger.LogWarning(
                    exception,
                    "Content verification failed for scan candidate {Path}",
                    LogRedaction.SanitizeFilePath(candidate));
                issues.Add(new ScanDiscoveryIssue(
                    ScanDiscoveryIssueKind.MetadataUnavailable,
                    candidate,
                    "Embedded metadata could not be read safely while verifying folder attribution."));
            }
        }

        return discovery with
        {
            AttributedFiles = attributed
                .OrderBy(path => path, semantics.Comparer)
                .ToList(),
            ProvenBookBoundaries = boundaries,
            Issues = issues,
            ContentDeclinedCandidates = contentDeclined
        };
    }

    private static bool IsOwnedOrLegacy(
        string path,
        IReadOnlySet<string> ownedCanonicalPaths,
        string? canonicalLegacyPath,
        FileSystemPathSemantics semantics)
    {
        var canonical = FileSystemPathIdentity.Canonicalize(path, semantics.Syntax);
        if (ownedCanonicalPaths.Contains(canonical))
        {
            return true;
        }

        return canonicalLegacyPath != null
            && FileSystemPathIdentity.AreEquivalent(
                canonical,
                canonicalLegacyPath,
                semantics);
    }

    private static string? ResolveCanonicalLegacyPath(
        Audiobook audiobook,
        FileSystemPathSemantics semantics)
    {
        if (string.IsNullOrWhiteSpace(audiobook.FilePath))
        {
            return null;
        }

        return TryResolveLegacyPath(
            audiobook,
            audiobook.FilePath,
            semantics,
            out var resolvedPath,
            out _)
            ? FileSystemPathIdentity.Canonicalize(resolvedPath, semantics.Syntax)
            : null;
    }
}
