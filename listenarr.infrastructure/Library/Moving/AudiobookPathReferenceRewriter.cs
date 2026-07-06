using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal static class AudiobookPathReferenceRewriter
{
    public static void Rewrite(
        Audiobook audiobook,
        string? sourceBasePath,
        string targetBasePath,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics)
    {
        ArgumentNullException.ThrowIfNull(audiobook);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetBasePath);

        if (!string.IsNullOrWhiteSpace(sourceBasePath))
        {
            audiobook.FilePath = RewriteAbsoluteReference(
                audiobook.FilePath,
                sourceBasePath,
                targetBasePath,
                sourceSemantics,
                targetSemantics);
            audiobook.ImageUrl = RewriteAbsoluteReference(
                audiobook.ImageUrl,
                sourceBasePath,
                targetBasePath,
                sourceSemantics,
                targetSemantics);

            foreach (var file in audiobook.Files ?? [])
            {
                file.Path = RewriteAbsoluteReference(
                    file.Path,
                    sourceBasePath,
                    targetBasePath,
                    sourceSemantics,
                    targetSemantics);
            }
        }

        audiobook.BasePath = targetBasePath;
    }

    private static string? RewriteAbsoluteReference(
        string? path,
        string sourceBasePath,
        string targetBasePath,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics)
    {
        if (string.IsNullOrWhiteSpace(path)
            || IsRemoteUri(path))
        {
            return path;
        }

        bool isInsideSource;
        try
        {
            isInsideSource = FileSystemPathIdentity.IsSameOrInside(path, sourceBasePath, sourceSemantics);
        }
        catch (ArgumentException)
        {
            // Relative and non-filesystem references are intentionally preserved.
            return path;
        }

        if (!isInsideSource)
        {
            return path;
        }

        if (!FileSystemPathIdentity.TryGetRelativePathWithinBase(
                sourceBasePath,
                path,
                sourceSemantics,
                out var relativePath)
            || !FileSystemPathIdentity.TryResolveRelativePathWithinBase(
                targetBasePath,
                FileSystemPathIdentity.ConvertRelativePathSyntax(
                    relativePath,
                    sourceSemantics.Syntax,
                    targetSemantics.Syntax),
                targetSemantics,
                out var rewrittenPath))
        {
            throw new InvalidOperationException(
                $"Stored audiobook path '{path}' could not be mapped to the new base path.");
        }

        return rewrittenPath;
    }

    private static bool IsRemoteUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile;
}
