using Listenarr.Application.Search.Discovery;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories;

public partial class AudiobookRepository
{
    public async Task<List<MonitoredBookIdentity>> GetMonitoredBookIdentitiesAsync(CancellationToken ct = default)
    {
        var rows = await _db.Audiobooks
            .AsNoTracking()
            .Where(b => b.Monitored && b.QualityProfileId != null)
            .Select(b => new { b.Id, b.Title, b.Authors, b.Asin })
            .ToListAsync(ct);

        return rows
            .Select(b => new MonitoredBookIdentity(b.Id, b.Title ?? string.Empty, b.Authors, b.Asin))
            .ToList();
    }
}
