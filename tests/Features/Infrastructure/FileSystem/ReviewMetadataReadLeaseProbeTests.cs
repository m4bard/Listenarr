using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

// REVIEW PROBE ONLY (not for upstream). Records whether the metadata-rescan lease factory
// hands out write-authorized leases on canary.
[Trait("Name", "ReviewMetadataReadLeaseProbeTests")]
[Trait("Category", "FileSystem")]
public sealed class ReviewMetadataReadLeaseProbeTests : BaseTests
{
    [LinuxFact]
    public async Task OpenForMetadataRead_NoExpectedIdentity_AuthorizesWrites()
    {
        var parentPath = FileService.GetTempDirectory("review-rescan-lease-strong");
        var publicPath = await FileService.GetFileAsync(parentPath, "book.m4b", "bytes");

        using var lease = PinnedAudiobookFileRegistrationLease.OpenForMetadataRead(
            publicPath,
            expectedPhysicalObjectIdentity: null);

        Assert.True(lease.SupportsMetadataWrite);
        using var stream = lease.OpenMetadataWriteStream();
        Assert.True(stream.CanWrite);
        Assert.False(stream.CanRead);
    }

    [LinuxFact]
    public async Task OpenForMetadataRead_WeakIdentity_DoesNotAuthorizeWrites()
    {
        var parentPath = FileService.GetTempDirectory("review-rescan-lease-weak");
        var publicPath = await FileService.GetFileAsync(parentPath, "book.m4b", "bytes");

        using var lease = PinnedAudiobookFileRegistrationLease.OpenForMetadataRead(
            publicPath,
            "linux-generation:00000008:00000001:0000000000001234:fh:00000081:341200000000000000000000");

        Assert.False(lease.SupportsMetadataWrite);
    }
}
