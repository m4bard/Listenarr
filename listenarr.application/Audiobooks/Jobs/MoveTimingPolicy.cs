namespace Listenarr.Application.Audiobooks.Jobs;

public static class MoveTimingPolicy
{
    public static readonly TimeSpan OwnershipDuration = TimeSpan.FromMinutes(2);
}
