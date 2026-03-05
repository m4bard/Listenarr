using System.IO;
using System.Text.RegularExpressions;

namespace Listenarr.Api.Services
{
    public class PathParsedMetadata
    {
        public string? Author { get; set; }
        public string? Series { get; set; }
        public string? SeriesNumber { get; set; }
        public string? Title { get; set; }
        public string? Year { get; set; }
        public string? Description { get; set; }
        public string? Narrator { get; set; }
        public string? CoverPath { get; set; }
    }

    /// <summary>
    /// Parses audiobook metadata from a folder path structure of the form:
    ///   {Root}/{Author}/{Series}/{Year} - {Title} [{Series} {Part}]/file.m4b
    /// or (standalone, no series folder):
    ///   {Root}/{Author}/{Year} - {Title}/file.m4b
    /// Also reads desc.txt (description) and reader.txt (narrator) sidecar files.
    /// </summary>
    public static class PathMetadataParser
    {
        // Matches folder names like:
        //   "2010 - The Way of Kings [The Stormlight Archive 1]"
        //   "1982 - The Gunslinger [The Dark Tower 1]"
        //   "2005 - Elantris"  (no series bracket)
        private static readonly Regex BookFolderPattern = new(
            @"^(\d{4})\s+-\s+(.+?)(?:\s+\[(.+?)\s+([\d.]+)\])?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] AudioExtensions = { ".m4b", ".mp3", ".flac", ".ogg", ".opus", ".m4a", ".aac", ".wav" };
        private static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        public static PathParsedMetadata Parse(string filePath, string rootFolderPath)
        {
            var result = new PathParsedMetadata();

            var normalizedFile = Path.GetFullPath(filePath);
            var normalizedRoot = Path.GetFullPath(rootFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return result;

            var relative = normalizedFile[(normalizedRoot.Length)..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            // parts[^1] is the filename; everything before is folder levels
            if (parts.Length < 2) return result;
            var folderParts = parts[..^1];

            // Find the first folder level that matches the "Year - Title" pattern
            int bookFolderIndex = -1;
            for (int i = 0; i < folderParts.Length; i++)
            {
                if (BookFolderPattern.IsMatch(folderParts[i]))
                {
                    bookFolderIndex = i;
                    break;
                }
            }

            if (bookFolderIndex < 0) return result;

            // Parse year, title, series, seriesNumber from the matched folder
            var m = BookFolderPattern.Match(folderParts[bookFolderIndex]);
            result.Year = m.Groups[1].Value;
            result.Title = m.Groups[2].Value.Trim();
            if (m.Groups[3].Success) result.Series = m.Groups[3].Value.Trim();
            if (m.Groups[4].Success) result.SeriesNumber = m.Groups[4].Value.Trim();

            // Assign Author and Series from path levels before the book folder
            if (bookFolderIndex == 1)
            {
                result.Author = folderParts[0];
            }
            else if (bookFolderIndex >= 2)
            {
                result.Author = folderParts[0];
                if (string.IsNullOrEmpty(result.Series))
                    result.Series = folderParts[1];
            }

            // Build absolute path to the book folder for sidecar reading
            var bookFolderPath = Path.Combine(normalizedRoot,
                Path.Combine(folderParts[..(bookFolderIndex + 1)]));

            TryReadSidecar(bookFolderPath, "desc.txt", content =>
                result.Description = content.Length > 2000 ? content[..2000] : content);

            TryReadSidecar(bookFolderPath, "reader.txt", content =>
                result.Narrator = content.Trim());

            // Find cover image
            try
            {
                result.CoverPath = Directory.EnumerateFiles(bookFolderPath)
                    .FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f).Contains("cover", StringComparison.OrdinalIgnoreCase) &&
                        CoverExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
            }
            catch { /* ignore */ }

            return result;
        }

        private static void TryReadSidecar(string folder, string filename, Action<string> assign)
        {
            try
            {
                var path = Path.Combine(folder, filename);
                if (File.Exists(path))
                    assign(File.ReadAllText(path).Trim());
            }
            catch { /* silently skip */ }
        }
    }
}
