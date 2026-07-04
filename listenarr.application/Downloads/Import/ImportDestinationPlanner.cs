using Listenarr.Domain.Common;

namespace Listenarr.Application.Downloads.Import;

public sealed class ImportDestinationPlanner(IFileSystem fileSystem)
{
    public bool TryResolve(
        string? basePath,
        string candidatePath,
        FileSystemPathSemantics destinationSemantics,
        out string destination)
    {
        destination = string.Empty;
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        return FileSystemPathIdentity.TryResolveRelativePathWithinBase(
            basePath,
            candidatePath.Trim(),
            destinationSemantics,
            out destination);
    }

    public async Task<string> ResolveIdempotentOrUniqueAsync(
        string sourcePath,
        string destination,
        ISet<string> usedDestinations,
        FileSystemPathSemantics destinationSemantics,
        CancellationToken cancellationToken = default)
    {
        if (destinationSemantics.CaseSensitivity == FileSystemCaseSensitivity.Unknown)
        {
            throw new InvalidOperationException("Destination filesystem case sensitivity must be resolved before import planning.");
        }

        if (fileSystem.FileExists(destination)
            && await fileSystem.FilesHaveSameContentAsync(sourcePath, destination, cancellationToken))
        {
            usedDestinations.Add(destination);
            return destination;
        }

        var uniqueDestination = FileUtils.GetUniqueDestinationPath(
            destination,
            fileSystem.FileExists,
            usedDestinations);
        usedDestinations.Add(uniqueDestination);
        return uniqueDestination;
    }

    public Task<bool> IsExistingEquivalentAsync(
        string sourcePath,
        string destination,
        CancellationToken cancellationToken = default) =>
        fileSystem.FileExists(destination)
            ? fileSystem.FilesHaveSameContentAsync(sourcePath, destination, cancellationToken)
            : Task.FromResult(false);
}
