using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Scanning;

internal enum ScanDiscoveryIssueKind
{
    EnumerationFailure,
    LinkSkipped,
    AttributionConflict,
    MetadataUnavailable,
    OutsideStableIdentifierBoundary,
    DirectoryGenerationChanged
}

internal sealed record ScanDiscoveryIssue(
    ScanDiscoveryIssueKind Kind,
    string? Path,
    string Message);

internal sealed record ScanDiscoveryResult(
    IReadOnlyList<string> Candidates,
    IReadOnlyList<string> AttributedFiles,
    IReadOnlyDictionary<string, string> ProvenBookBoundaries,
    IReadOnlyList<string> EnumeratedDirectories,
    IReadOnlyDictionary<string, string> DirectoryObjectIdentities,
    IReadOnlyDictionary<string, string> FileObjectIdentities,
    string? SelectedStableIdentifierBoundary,
    bool HasStableIdentifierBoundaryConflict,
    IReadOnlyList<ScanDiscoveryIssue> Issues)
{
    /// <summary>
    /// Candidates already owned by a different audiobook. They stay in
    /// <see cref="Candidates"/> for enumeration bookkeeping but are never claimable.
    /// Discovery fills it with the scan's semantic comparer. The default is only ever
    /// empty, and an empty set answers Contains the same under any comparer.
    /// </summary>
    public IReadOnlySet<string> ForeignOwnedCandidates { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Candidates that folder/path-name evidence attributed to this audiobook, but whose
    /// embedded tags named a different book and author, so content verification removed
    /// them from <see cref="AttributedFiles"/>. Kept distinct from <see cref="Issues"/>
    /// (which feed completeness/attribution-conflict decisions) so a later pass, such as
    /// <c>EnrichWithMetadataAsync</c>, can skip re-probing a file that was already
    /// declined on content grounds. Same default-comparer convention as
    /// <see cref="ForeignOwnedCandidates"/>.
    /// </summary>
    public IReadOnlySet<string> ContentDeclinedCandidates { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    public bool IsComplete => Issues.All(issue =>
        issue.Kind is not (ScanDiscoveryIssueKind.EnumerationFailure
            or ScanDiscoveryIssueKind.LinkSkipped
            or ScanDiscoveryIssueKind.DirectoryGenerationChanged));

    public bool HasAttributionConflict => Issues.Any(issue =>
        issue.Kind == ScanDiscoveryIssueKind.AttributionConflict);

    public bool CanReconcile => IsComplete;

    public bool CanUpdateBasePath => IsComplete && !HasAttributionConflict;

    public string? CommonProvenBookBoundary(FileSystemPathSemantics semantics)
    {
        if (!string.IsNullOrWhiteSpace(SelectedStableIdentifierBoundary))
        {
            return SelectedStableIdentifierBoundary;
        }

        var boundaries = AttributedFiles
            .Select(path => ProvenBookBoundaries.TryGetValue(path, out var boundary)
                ? boundary
                : null)
            .Where(boundary => !string.IsNullOrWhiteSpace(boundary))
            .Distinct(semantics.Comparer)
            .ToList();
        return boundaries.Count == 1 ? boundaries[0] : null;
    }
}
