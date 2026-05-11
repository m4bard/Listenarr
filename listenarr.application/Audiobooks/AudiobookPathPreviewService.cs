using System.Text.RegularExpressions;
using Listenarr.Application.Naming;
using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;

namespace Listenarr.Application.Audiobooks
{
    public class AudiobookPathPreviewService(
        IApplicationSettingsRepository settingsRepository,
        INamingPatternService namingPatternService) : IAudiobookPathPreviewService
    {
        public async Task<PathPreviewResult> PreviewAsync(
            Audiobook audiobook,
            string? destinationRoot = null,
            CancellationToken ct = default)
        {
            var settings = await settingsRepository.GetAsync(ct) ?? new ApplicationSettings();
            var root = !string.IsNullOrWhiteSpace(destinationRoot)
                ? destinationRoot
                : settings.OutputPath;

            var namingPattern = !string.IsNullOrWhiteSpace(settings.FolderNamingPattern)
                ? settings.FolderNamingPattern
                : settings.FileNamingPattern;

            var full = ComputeBaseDirectoryFromPattern(audiobook, root ?? string.Empty, namingPattern);
            var relative = full;

            if (!string.IsNullOrEmpty(root) && full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                relative = full.Substring(root.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return new PathPreviewResult(full, relative, root ?? string.Empty);
        }

        private string ComputeBaseDirectoryFromPattern(
            Audiobook audiobook,
            string rootPath,
            string fileNamingPattern)
        {
            var directoryPattern = BuildDirectoryPattern(fileNamingPattern, audiobook);

            var variables = new Dictionary<string, object>
            {
                { "Author", namingPatternService.SanitizePathComponent(audiobook.Authors?.FirstOrDefault() ?? "Unknown Author") },
                { "Series", SanitizeOptional(audiobook.Series) },
                { "Title", namingPatternService.SanitizePathComponent(audiobook.Title ?? "Unknown Title") },
                { "Subtitle", SanitizeOptional(audiobook.Subtitle) },
                { "Edition", SanitizeOptional(audiobook.Edition) },
                { "Narrator", SanitizeOptional((audiobook.Narrators != null && audiobook.Narrators.Any()) ? string.Join(", ", audiobook.Narrators.Where(n => !string.IsNullOrWhiteSpace(n))) : string.Empty) },
                { "Publisher", SanitizeOptional(audiobook.Publisher) },
                { "Language", SanitizeOptional(audiobook.Language) },
                { "Asin", SanitizeOptional(audiobook.Asin) },
                { "SeriesNumber", audiobook.SeriesNumber ?? string.Empty },
                { "Year", audiobook.PublishYear ?? string.Empty },
                { "Quality", string.Empty },
                { "DiskNumber", string.Empty },
                { "ChapterNumber", string.Empty }
            };

            var relative = namingPatternService.ApplyNamingPattern(directoryPattern, variables);
            return ResolvePathWithOptionalBase(rootPath, relative);
        }

        private string SanitizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : namingPatternService.SanitizePathComponent(value);
        }

        private static string BuildDirectoryPattern(string fileNamingPattern, Audiobook audiobook)
        {
            string directoryPattern;
            if (!string.IsNullOrWhiteSpace(fileNamingPattern))
            {
                directoryPattern = fileNamingPattern;
                directoryPattern = Regex.Replace(directoryPattern, @"\{DiskNumber[^}]*\}", string.Empty, RegexOptions.IgnoreCase);
                directoryPattern = Regex.Replace(directoryPattern, @"\{ChapterNumber[^}]*\}", string.Empty, RegexOptions.IgnoreCase);
                directoryPattern = Regex.Replace(directoryPattern, @"[\\/]\s*[\\/]", "/");
                directoryPattern = Regex.Replace(directoryPattern, @"^\s*[\\/]", string.Empty);
                directoryPattern = Regex.Replace(directoryPattern, @"[\\/]\s*$", string.Empty);

                if (string.IsNullOrWhiteSpace(directoryPattern) || !directoryPattern.Contains('/'))
                {
                    directoryPattern = "{Author}/{Title}";
                }
            }
            else
            {
                directoryPattern = "{Author}/{Title}";
            }

            if (!string.IsNullOrWhiteSpace(audiobook.Series) && !directoryPattern.Contains("{Series}"))
            {
                if (directoryPattern.Contains("{Author}/{Title}"))
                {
                    directoryPattern = directoryPattern.Replace("{Author}/{Title}", "{Author}/{Series}/{Title}");
                }
                else if (directoryPattern.Contains("{Author}/"))
                {
                    directoryPattern = directoryPattern.Replace("{Author}/", "{Author}/{Series}/");
                }
            }

            if (string.IsNullOrWhiteSpace(audiobook.Series))
            {
                directoryPattern = Regex.Replace(directoryPattern, @"\{Series[^}]*\}", string.Empty, RegexOptions.IgnoreCase);
                directoryPattern = Regex.Replace(directoryPattern, @"[\\/]\s*[\\/]", "/");
                directoryPattern = Regex.Replace(directoryPattern, @"^\s*[\\/]", string.Empty);
                directoryPattern = Regex.Replace(directoryPattern, @"[\\/]\s*$", string.Empty);
            }

            return directoryPattern;
        }

        private static string ResolvePathWithOptionalBase(string? basePath, string candidatePath)
        {
            var normalizedPath = candidatePath.Trim();

            if (string.IsNullOrEmpty(normalizedPath))
            {
                return normalizedPath;
            }

            if (Path.IsPathRooted(normalizedPath) || string.IsNullOrWhiteSpace(basePath))
            {
                return normalizedPath;
            }

            var relativePath = normalizedPath.TrimStart(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            if (Path.IsPathRooted(relativePath))
            {
                return relativePath;
            }

            var normalizedBasePath = basePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            return string.IsNullOrEmpty(normalizedBasePath)
                ? relativePath
                : normalizedBasePath + Path.DirectorySeparatorChar + relativePath;
        }
    }
}
