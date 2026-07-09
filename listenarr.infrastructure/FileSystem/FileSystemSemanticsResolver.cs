using System.Collections.Concurrent;
using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.FileSystem;

public sealed class FileSystemSemanticsResolver : IFileSystemSemanticsResolver
{
    private const string ProbePrefix = ".listenarr-case-probe-";
    private readonly ConcurrentDictionary<string, FileSystemSemanticsResolution> _cache = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public ValueTask<FileSystemSemanticsResolution> ResolveAsync(
        string path,
        FileSystemCaseSensitivityMode mode = FileSystemCaseSensitivityMode.Auto,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Filesystem semantics require an absolute path.", nameof(path));
        }

        var syntax = OperatingSystem.IsWindows()
            ? FileSystemPathSyntax.Windows
            : FileSystemPathSyntax.Unix;
        var fullPath = Path.GetFullPath(path);

        if (mode != FileSystemCaseSensitivityMode.Auto)
        {
            var explicitSensitivity = mode == FileSystemCaseSensitivityMode.Sensitive
                ? FileSystemCaseSensitivity.Sensitive
                : FileSystemCaseSensitivity.Insensitive;
            return ValueTask.FromResult(new FileSystemSemanticsResolution(
                new FileSystemPathSemantics(syntax, explicitSensitivity),
                PathIdentityState.Valid,
                FindExistingBoundary(fullPath) ?? Path.GetPathRoot(fullPath) ?? fullPath));
        }

        var boundary = FindExistingBoundary(fullPath);
        if (boundary == null)
        {
            return ValueTask.FromResult(Unavailable(syntax, fullPath, "No existing filesystem boundary could be found."));
        }

        if (_cache.TryGetValue(boundary, out var cached))
        {
            return ValueTask.FromResult(cached);
        }

        var resolved = TryReadOnlyProbe(boundary, syntax) ?? Probe(boundary, syntax);
        if (resolved.State == PathIdentityState.Valid)
        {
            _cache[boundary] = resolved;
        }

        return ValueTask.FromResult(resolved);
    }

    private static FileSystemSemanticsResolution? TryReadOnlyProbe(
        string boundary,
        FileSystemPathSyntax syntax)
    {
        var trimmedBoundary = boundary.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(trimmedBoundary))
        {
            return null;
        }

        var parent = Path.GetDirectoryName(trimmedBoundary);
        var leaf = Path.GetFileName(trimmedBoundary);
        if (string.IsNullOrEmpty(parent)
            || string.IsNullOrEmpty(leaf)
            || !leaf.Any(char.IsLetter))
        {
            return null;
        }

        try
        {
            var caseVariants = Directory
                .EnumerateFileSystemEntries(parent)
                .Select(Path.GetFileName)
                .Where(name => string.Equals(name, leaf, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (caseVariants.Count > 1)
            {
                return new FileSystemSemanticsResolution(
                    new FileSystemPathSemantics(syntax, FileSystemCaseSensitivity.Sensitive),
                    PathIdentityState.Valid,
                    boundary);
            }

            if (caseVariants.Count == 0)
            {
                return null;
            }

            var alternateLeaf = BuildAlternateCaseProbeName(leaf);
            if (string.Equals(alternateLeaf, leaf, StringComparison.Ordinal))
            {
                return null;
            }

            var alternatePath = Path.Combine(parent, alternateLeaf);
            var sensitivity = Directory.Exists(alternatePath) || File.Exists(alternatePath)
                ? FileSystemCaseSensitivity.Insensitive
                : FileSystemCaseSensitivity.Sensitive;
            return new FileSystemSemanticsResolution(
                new FileSystemPathSemantics(syntax, sensitivity),
                PathIdentityState.Valid,
                boundary);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string BuildAlternateCaseProbeName(string value)
    {
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (char.IsUpper(chars[index]))
            {
                chars[index] = char.ToLowerInvariant(chars[index]);
                return new string(chars);
            }

            if (char.IsLower(chars[index]))
            {
                chars[index] = char.ToUpperInvariant(chars[index]);
                return new string(chars);
            }
        }

        return value;
    }

    private static FileSystemSemanticsResolution Probe(
        string boundary,
        FileSystemPathSyntax syntax)
    {
        var probeName = ProbePrefix + Guid.NewGuid().ToString("N") + "-a";
        var probePath = Path.Join(boundary, probeName);
        var alternatePath = Path.Join(boundary, probeName.ToUpperInvariant());
        try
        {
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
            }

            var sensitivity = File.Exists(alternatePath)
                ? FileSystemCaseSensitivity.Insensitive
                : FileSystemCaseSensitivity.Sensitive;
            return new FileSystemSemanticsResolution(
                new FileSystemPathSemantics(syntax, sensitivity),
                PathIdentityState.Valid,
                boundary);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Unavailable(
                syntax,
                boundary,
                $"Filesystem case sensitivity could not be probed: {exception.GetType().Name}.");
        }
        finally
        {
            TryDeleteProbe(probePath);
            if (!string.Equals(probePath, alternatePath, StringComparison.Ordinal))
            {
                TryDeleteProbe(alternatePath);
            }
        }
    }

    private static string? FindExistingBoundary(string path)
    {
        // Callers provide validated, absolute administrative filesystem paths. Probing the
        // nearest existing ancestor is intentional: automatic case-sensitivity detection must
        // work before a configured library destination exists. This must not be exposed as a
        // general-purpose path-existence oracle for unauthenticated input.
        var current = path;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static FileSystemSemanticsResolution Unavailable(
        FileSystemPathSyntax syntax,
        string boundary,
        string reason)
    {
        return new FileSystemSemanticsResolution(
            new FileSystemPathSemantics(syntax, FileSystemCaseSensitivity.Unknown),
            PathIdentityState.Unavailable,
            boundary,
            reason);
    }

    private static void TryDeleteProbe(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Failed to remove filesystem case-sensitivity probe {0}: {1}",
                path,
                exception.Message);
        }
    }
}
