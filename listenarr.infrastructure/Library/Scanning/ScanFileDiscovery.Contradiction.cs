namespace Listenarr.Infrastructure.Library.Scanning;

internal static partial class ScanFileDiscovery
{
    // A folder/path-name match from Discover() is strong evidence of location, not of
    // content. When the file's own embedded tags clearly name a different book by a
    // different author, that content evidence overrides the folder match. ASIN is
    // deliberately never consulted here: the production ffprobe mapper never populates
    // it, so using it for contradiction would be dead code and a false-positive risk
    // across regional ASIN variants of the same book.
    internal static bool MetadataContradictsAudiobook(
        AudioMetadata metadata,
        Audiobook audiobook,
        string candidatePath)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(audiobook);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);

        if (MetadataMatchesAudiobook(metadata, audiobook))
        {
            return false;
        }

        return TitleSideDisagrees(metadata, audiobook, candidatePath)
            && AuthorSideDisagrees(metadata, audiobook);
    }

    private static bool TitleSideDisagrees(
        AudioMetadata metadata,
        Audiobook audiobook,
        string candidatePath)
    {
        var stem = NormalizeMetadataToken(
            Path.GetFileNameWithoutExtension(candidatePath));
        var evidence = new List<string>();
        var album = NormalizeMetadataToken(metadata.Album);
        if (!string.IsNullOrEmpty(album))
        {
            evidence.Add(album);
        }

        var title = NormalizeMetadataToken(metadata.Title);
        if (!string.IsNullOrEmpty(title)
            && !string.Equals(title, stem, StringComparison.Ordinal))
        {
            // A Title that merely echoes the filename is the ffprobe fallback, not
            // real tag evidence, and must not be used to contradict a folder match.
            evidence.Add(title);
        }

        if (evidence.Count == 0)
        {
            return false;
        }

        var expected = NormalizeMetadataToken(audiobook.Title);
        if (string.IsNullOrEmpty(expected))
        {
            return false;
        }

        return evidence.All(value =>
            !WordContains(value, expected) && !WordContains(expected, value));
    }

    private static bool AuthorSideDisagrees(
        AudioMetadata metadata,
        Audiobook audiobook)
    {
        var authors = BuildExpectedAuthorTokens(audiobook);
        if (authors.Count == 0)
        {
            return false;
        }

        var fields = new List<string>();
        var albumArtist = NormalizeMetadataToken(metadata.AlbumArtist);
        if (!string.IsNullOrEmpty(albumArtist))
        {
            fields.Add(albumArtist);
        }

        var artist = NormalizeMetadataToken(metadata.Artist);
        if (!string.IsNullOrEmpty(artist))
        {
            fields.Add(artist);
        }

        if (fields.Count == 0)
        {
            return false;
        }

        // Disagreement requires every field to fail to contain every expected author's
        // words, so a "Last, First" reordering or a narrator appended to the artist tag
        // (whose words are a superset of the author's) is never read as a contradiction.
        return fields.All(field =>
            authors.All(author => !IsWordSubset(author, field)));
    }

    private static bool WordContains(string haystack, string needle) =>
        $" {haystack} ".Contains($" {needle} ", StringComparison.Ordinal);

    private static bool IsWordSubset(string needle, string haystack)
    {
        var needleWords = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (needleWords.Length == 0)
        {
            return false;
        }

        var haystackWords = new HashSet<string>(
            haystack.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);
        return needleWords.All(haystackWords.Contains);
    }
}
