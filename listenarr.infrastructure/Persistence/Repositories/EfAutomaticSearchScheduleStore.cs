using Listenarr.Application.Search.Scheduling;

namespace Listenarr.Infrastructure.Persistence.Repositories;

public sealed class EfAutomaticSearchScheduleStore(ListenArrDbContext db) : IAutomaticSearchScheduleStore
{
    public Task<IReadOnlyList<Audiobook>> GetDuePageAsync(
        DateTime nowUtc,
        IReadOnlyCollection<int> excludedIds,
        int pageSize,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Audiobook>>([]);

    public Task<IReadOnlyList<AudiobookIndexerSearchState>> GetPairStatesAsync(
        IReadOnlyCollection<int> audiobookIds,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AudiobookIndexerSearchState>>([]);
}
