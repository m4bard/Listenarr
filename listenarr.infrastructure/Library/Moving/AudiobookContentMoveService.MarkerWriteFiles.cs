namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private readonly record struct MarkerWriteIdentity(
        Guid JobId,
        int LeaseGeneration);

    private sealed class InterruptedOwnershipPublicationException(string message)
        : IOException(message);

    private static string CreateMarkerWritePath(
        string markerPath,
        Guid jobId,
        int leaseGeneration) =>
        markerPath
        + $".writing-{jobId:N}-g{leaseGeneration}-{Guid.NewGuid():N}";

    private static bool TryParseMarkerWriteIdentity(
        string writePath,
        string markerPath,
        out MarkerWriteIdentity identity)
    {
        identity = default;
        var fileName = Path.GetFileName(writePath);
        var prefix = Path.GetFileName(markerPath) + ".writing-";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = fileName[prefix.Length..];
        var generationSeparator = suffix.IndexOf("-g", StringComparison.Ordinal);
        if (generationSeparator != 32
            || !Guid.TryParseExact(suffix[..generationSeparator], "N", out var jobId))
        {
            return false;
        }

        var uniqueSeparator = suffix.IndexOf('-', generationSeparator + 2);
        if (uniqueSeparator <= generationSeparator + 2
            || !int.TryParse(
                suffix.AsSpan(generationSeparator + 2, uniqueSeparator - generationSeparator - 2),
                out var leaseGeneration)
            || leaseGeneration <= 0
            || !Guid.TryParseExact(suffix[(uniqueSeparator + 1)..], "N", out _))
        {
            return false;
        }

        identity = new MarkerWriteIdentity(jobId, leaseGeneration);
        return true;
    }
}
