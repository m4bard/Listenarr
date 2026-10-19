namespace Listenarr.Tests.Mocks;

/// <summary>
/// Delegates every member to a real registration lease except SupportsMetadataWrite, which it
/// reports as granted. Manual import only enriches tags under a lease that authorizes metadata
/// writes, and the markerless publication path withholds that authority, so a test of the
/// enrichment decision itself needs a lease that grants it. Publication probing and identity
/// matching fall back exactly as production does for a lease that implements neither.
/// </summary>
internal sealed class MetadataWriteAuthorizedRegistrationLease(
    IAudiobookFileRegistrationLease inner) :
    IAudiobookFileRegistrationLease,
    IAudiobookFileRegistrationIdentityVerifier,
    IAudiobookFileRegistrationPublicationProbe
{
    public string PublicPath => inner.PublicPath;

    public string MetadataPath => inner.MetadataPath;

    public string PhysicalObjectIdentity => inner.PhysicalObjectIdentity;

    public bool HasDurablePhysicalObjectIdentity => inner.HasDurablePhysicalObjectIdentity;

    public bool SupportsMetadataWrite => true;

    public string? SourcePhysicalObjectIdentity => inner.SourcePhysicalObjectIdentity;

    public Stream OpenMetadataReadStream() => inner.OpenMetadataReadStream();

    public Stream OpenMetadataWriteStream() => inner.OpenMetadataWriteStream();

    public bool MatchesCurrentPublication() => inner.MatchesCurrentPublication();

    public bool PrepareCleanupRecovery(int audiobookId) =>
        inner.PrepareCleanupRecovery(audiobookId);

    public RegistrationPublicationCompletion CompletePublication() =>
        inner.CompletePublication();

    public Task<bool> MatchesContentAsync(
        Stream candidateStream,
        CancellationToken cancellationToken = default) =>
        inner.MatchesContentAsync(candidateStream, cancellationToken);

    public bool MatchesPhysicalObjectIdentity(string expectedPhysicalObjectIdentity) =>
        inner.MatchesPhysicalObjectIdentity(expectedPhysicalObjectIdentity);

    public RegistrationPublicationMatchOutcome ProbeCurrentPublication() =>
        inner is IAudiobookFileRegistrationPublicationProbe probe
            ? probe.ProbeCurrentPublication()
            : inner.MatchesCurrentPublication()
                ? RegistrationPublicationMatchOutcome.Match
                : RegistrationPublicationMatchOutcome.Mismatch;

    public void Dispose() => inner.Dispose();
}
