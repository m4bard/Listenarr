using Listenarr.Infrastructure.FileSystem;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

// Probe only. Not for upstream. Measures whether a persisted Linux physical identity
// survives a change of st_dev while inode and export file handle are unchanged, which
// is the shape reported in Listenarr#947 (CephFS, per-node mount device number) and
// plausibly in the NFS remount report on the same issue.
//
// Device numbers below are the ones in the #947 report: st_dev 0x100016 on node A and
// 0x2000af on node B, both major 0, minors 278 and 687. Inode 1099512613376 is the one
// the reporter measured on both nodes. The generation evidence is a filesystem specific
// handle type 0x00000001 (FILEID_INO32_GEN), which is what CephFS emits, held identical
// across the two nodes.
[Trait("Name", "DeviceNumberIdentityProbeTests")]
[Trait("Category", "Infrastructure")]
public sealed class DeviceNumberIdentityProbeTests
{
    private const uint MajorBoth = 0;
    private const uint MinorNodeA = 278;
    private const uint MinorNodeB = 687;
    private const ulong SharedInode = 1099512613376UL;
    private const string SharedHandle = "fh:00000001:0011223344556677";

    private static string Identity(uint deviceMinor, ulong inode, string generation) =>
        PinnedDirectoryCreation.CreateLinuxObjectIdentityFromEvidence(
            deviceMajor: MajorBoth,
            deviceMinor: deviceMinor,
            inode: inode,
            hasBirthTime: false,
            birthTimeSeconds: 0,
            birthTimeNanoseconds: 0,
            generationIdentity: generation);

    [Fact]
    public void DeviceNumberIsCarriedIntoThePersistedIdentityString()
    {
        var nodeA = Identity(MinorNodeA, SharedInode, SharedHandle);
        var nodeB = Identity(MinorNodeB, SharedInode, SharedHandle);

        Assert.Equal(
            "linux-generation:00000000:00000116:00000100000f0a00:fh:00000001:0011223344556677",
            nodeA);
        Assert.Equal(
            "linux-generation:00000000:000002af:00000100000f0a00:fh:00000001:0011223344556677",
            nodeB);
        Assert.NotEqual(nodeA, nodeB);
    }

    [Fact]
    public void SameObjectSeenThroughADifferentMountDeviceIsNotDurablyEquivalent()
    {
        var nodeA = Identity(MinorNodeA, SharedInode, SharedHandle);
        var nodeB = Identity(MinorNodeB, SharedInode, SharedHandle);

        // The defect under test.
        Assert.False(
            PinnedDirectoryCreation.ArePersistedObjectIdentitiesDurablyEquivalent(nodeA, nodeB));
    }

    [Fact]
    public void ControlIdenticalEvidenceIsEquivalent()
    {
        // Apparatus control. If this were also false the function would be refusing
        // everything and the test above would prove nothing.
        var nodeA = Identity(MinorNodeA, SharedInode, SharedHandle);

        Assert.True(
            PinnedDirectoryCreation.ArePersistedObjectIdentitiesDurablyEquivalent(nodeA, nodeA));
    }

    [Fact]
    public void ControlDifferentSpellingOfTheSameEvidenceIsEquivalent()
    {
        // Second apparatus control, and the one that matters: the comparison is not a
        // plain string equality. It can see through a different persisted spelling of the
        // same object, so the false above is a scoping decision about the device fields
        // rather than an inability to compare at all.
        var candidates = PinnedDirectoryCreation.CreateLinuxObjectIdentityCandidatesFromEvidence(
            deviceMajor: MajorBoth,
            deviceMinor: MinorNodeA,
            inode: SharedInode,
            hasBirthTime: true,
            birthTimeSeconds: 0x5678,
            birthTimeNanoseconds: 0x9abc,
            generationIdentities: [SharedHandle]);
        var strong = candidates[0];
        var mergedV1 = candidates[1];

        Assert.StartsWith("linux-generation:", strong, StringComparison.Ordinal);
        Assert.StartsWith("linux:", mergedV1, StringComparison.Ordinal);
        Assert.NotEqual(strong, mergedV1);
        Assert.True(
            PinnedDirectoryCreation.ArePersistedObjectIdentitiesDurablyEquivalent(
                strong,
                mergedV1));
    }

    [Fact]
    public void ControlGenuinelyDifferentObjectIsNotEquivalent()
    {
        // Negative control that must come out differently from the positive one.
        var nodeA = Identity(MinorNodeA, SharedInode, SharedHandle);
        var otherObject = Identity(MinorNodeA, SharedInode + 1, "fh:00000001:7766554433221100");

        Assert.False(
            PinnedDirectoryCreation.ArePersistedObjectIdentitiesDurablyEquivalent(
                nodeA,
                otherObject));
    }
}
