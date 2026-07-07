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
            candidatePath,
            destinationSemantics,
            out destination);
    }

    public async Task<ImportDestinationReservation> PlanIdempotentOrUniqueAsync(
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
            return new ImportDestinationReservation(destination);
        }

        var uniqueDestination = FileUtils.GetUniqueDestinationPath(
            destination,
            fileSystem.FileExists,
            usedDestinations);
        return new ImportDestinationReservation(uniqueDestination);
    }

    public static void Commit(
        ImportDestinationReservation reservation,
        ISet<string> usedDestinations)
    {
        usedDestinations.Add(reservation.Path);
    }

    public Task<bool> IsExistingEquivalentAsync(
        string sourcePath,
        string destination,
        CancellationToken cancellationToken = default) =>
        fileSystem.FileExists(destination)
            ? fileSystem.FilesHaveSameContentAsync(sourcePath, destination, cancellationToken)
            : Task.FromResult(false);
}

public sealed record ImportDestinationReservation(string Path);
