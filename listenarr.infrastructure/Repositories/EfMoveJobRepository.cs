using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Repositories
{
    public class EfMoveJobRepository : IMoveJobRepository
    {
        private readonly ListenArrDbContext _db;

        public EfMoveJobRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<MoveJob?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return await _db.MoveJobs.FindAsync(new object[] { id }, ct);
        }

        public async Task<List<MoveJob>> GetByStatusAsync(IEnumerable<string> statuses, CancellationToken ct = default)
        {
            return await _db.MoveJobs
                .AsNoTracking()
                .Where(j => statuses.Contains(j.Status))
                .ToListAsync(ct);
        }

        public async Task<MoveJob> AddAsync(MoveJob job, CancellationToken ct = default)
        {
            _db.MoveJobs.Add(job);
            await _db.SaveChangesAsync(ct);
            return job;
        }

        public async Task UpdateAsync(MoveJob job, CancellationToken ct = default)
        {
            _db.MoveJobs.Update(job);
            await _db.SaveChangesAsync(ct);
        }
    }
}
