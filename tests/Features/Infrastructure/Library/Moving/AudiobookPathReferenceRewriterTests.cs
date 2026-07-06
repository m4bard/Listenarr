/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public sealed class AudiobookPathReferenceRewriterTests
{
    [Fact]
    public void Rewrite_UsesSourceAndTargetSyntaxWithoutHostPathConversion()
    {
        var audiobook = new Audiobook
        {
            BasePath = "C:\\Library\\Book",
            FilePath = "c:/library/book/book.m4b",
            ImageUrl = "https://example.test/cover.jpg",
            Files =
            [
                new AudiobookFile { Path = "C:\\LIBRARY\\BOOK\\disc-1\\chapter.mp3" },
                new AudiobookFile { Path = "disc-2/chapter.mp3" },
                new AudiobookFile { Path = "C:\\Other\\bonus.mp3" }
            ]
        };
        var sourceSemantics = new FileSystemPathSemantics(
            FileSystemPathSyntax.Windows,
            FileSystemCaseSensitivity.Insensitive);
        var targetSemantics = new FileSystemPathSemantics(
            FileSystemPathSyntax.Unix,
            FileSystemCaseSensitivity.Sensitive);

        AudiobookPathReferenceRewriter.Rewrite(
            audiobook,
            "C:\\Library\\Book",
            "/library/Book",
            sourceSemantics,
            targetSemantics);

        Assert.Equal(FileUtils.NormalizeStoredPath("/library/Book"), audiobook.BasePath);
        Assert.Equal("/library/Book/book.m4b", audiobook.FilePath);
        Assert.Equal("https://example.test/cover.jpg", audiobook.ImageUrl);
        Assert.Equal("/library/Book/disc-1/chapter.mp3", audiobook.Files![0].Path);
        Assert.Equal("disc-2/chapter.mp3", audiobook.Files[1].Path);
        Assert.Equal("C:\\Other\\bonus.mp3", audiobook.Files[2].Path);
    }

    [Fact]
    public void Rewrite_SensitiveSourceDoesNotRemapDifferentCase()
    {
        var audiobook = new Audiobook
        {
            BasePath = "/Library/Book",
            FilePath = "/library/Book/book.m4b"
        };
        var semantics = new FileSystemPathSemantics(
            FileSystemPathSyntax.Unix,
            FileSystemCaseSensitivity.Sensitive);

        AudiobookPathReferenceRewriter.Rewrite(
            audiobook,
            "/Library/Book",
            "/target/Book",
            semantics,
            semantics);

        Assert.Equal(FileUtils.NormalizeStoredPath("/target/Book"), audiobook.BasePath);
        Assert.Equal("/library/Book/book.m4b", audiobook.FilePath);
    }

    [Fact]
    public void Rewrite_ThrowsWhenInScopeReferenceCannotBeMapped()
    {
        var audiobook = new Audiobook
        {
            BasePath = "/library/Book",
            FilePath = "/library/Book"
        };
        var semantics = new FileSystemPathSemantics(
            FileSystemPathSyntax.Unix,
            FileSystemCaseSensitivity.Sensitive);

        Assert.Throws<InvalidOperationException>(() => AudiobookPathReferenceRewriter.Rewrite(
            audiobook,
            "/library/Book",
            "/target/Book",
            semantics,
            semantics));
    }
}
