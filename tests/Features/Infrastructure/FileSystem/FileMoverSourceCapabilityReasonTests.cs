using System.ComponentModel;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

/// <summary>
/// The refusal string this gate hands back is the only thing an operator gets when a source file
/// cannot be pinned, and nothing downstream sanitizes it again before it reaches the activity
/// API. These assert that the cause is spelled the way the import record spells it, that a
/// linked ancestor is reduced to a filename rather than its full path, and that neither half can
/// forge a second record.
/// </summary>
[Trait("Area", "FileSystem")]
[Trait("Name", "FileMoverSourceCapabilityReasonTests")]
[Trait("Category", "Infrastructure")]
public sealed class FileMoverSourceCapabilityReasonTests : BaseTests
{
    [Fact]
    public void ComposeUnsupportedReason_LeadsWithTheCause()
    {
        var reason = FileMover.ComposeUnsupportedReason(
            new Win32Exception("Could not open a newly created pinned directory."),
            linkedAncestor: null);

        Assert.StartsWith("Win32Exception: Could not open a newly created pinned directory.", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeUnsupportedReason_SymlinkAdvice_SurvivesNeitherHalfBeingDropped()
    {
        // The realistic shape: a pooled mount reached through a link, with a path long enough
        // that the whole reason exceeds what a consumer will render.
        var reason = FileMover.ComposeUnsupportedReason(
            new Win32Exception("Could not open a newly created pinned directory."),
            linkedAncestor: "/mnt/pool/media/library/downloads/completed/audiobooks");

        Assert.Contains("symbolic link", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("configure the real path", reason, StringComparison.OrdinalIgnoreCase);

        // The segment is a path, not free text, so only its filename survives: the host's
        // directory layout does not belong in a message that reaches the activity API. This is
        // the regression this test exists to catch (see #975).
        Assert.DoesNotContain("/mnt/pool/media/library/downloads/completed/audiobooks", reason, StringComparison.Ordinal);
        Assert.Contains("'audiobooks'", reason, StringComparison.Ordinal);

        // The point of the ordering. Every consumer renders the reason as-is, so if something
        // downstream still truncates it, the cause (which cannot be reconstructed from anywhere
        // else) has to be the half that survives, not the fixed advice sentence.
        Assert.True(reason.Length > 200, $"the case is only meaningful when truncation bites: {reason.Length}");
        var rendered = LogRedaction.SanitizeText(reason);
        Assert.Contains("Win32Exception: Could not open a newly created pinned directory.", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeUnsupportedReason_UsesTheSharedCauseSpelling_NotItsOwn()
    {
        // The seam. While this site formatted its own cause it saw the outer exception only, so a
        // wrapped failure arriving here lost its reason while the same failure on the import path
        // kept it. Both now read the chain the same way.
        var native = new Win32Exception("Could not open a newly created pinned directory.");
        var wrapped = new IOException("Unable to pin the source directory", native);

        var reason = FileMover.ComposeUnsupportedReason(wrapped, linkedAncestor: null);

        Assert.StartsWith(ExceptionCause.Describe(wrapped), reason, StringComparison.Ordinal);
        Assert.Contains("Could not open a newly created pinned directory.", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeUnsupportedReason_NewlineInTheMessage_CannotForgeASecondRecord()
    {
        // A download client picks the file name, and the file name is what most of these
        // exception messages quote.
        var reason = FileMover.ComposeUnsupportedReason(
            new IOException("book.m4b\nfatal: everything is fine"),
            linkedAncestor: null);

        Assert.DoesNotContain('\n', reason);
        Assert.DoesNotContain('\r', reason);
    }

    [Fact]
    public void ComposeUnsupportedReason_NewlineInTheLinkedAncestor_CannotForgeASecondRecord()
    {
        // The linked-ancestor segment is as attacker-influenced as the exception message: a
        // download client names the directories under it. Reducing it to a filename with
        // SanitizeFilePath does not by itself strip a newline inside that filename, so this
        // checks the composition still does.
        var reason = FileMover.ComposeUnsupportedReason(
            new Win32Exception("Could not open a newly created pinned directory."),
            linkedAncestor: "/mnt/pool/book.m4b\nfatal: everything is fine");

        Assert.DoesNotContain('\n', reason);
        Assert.DoesNotContain('\r', reason);
    }
}
