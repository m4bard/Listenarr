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
    public class EfProcessExecutionLogRepository : IProcessExecutionLogRepository
    {
        private readonly ListenArrDbContext _db;

        public EfProcessExecutionLogRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task AddAsync(ProcessExecutionLog log, CancellationToken ct = default)
        {
            _db.ProcessExecutionLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<List<ProcessExecutionLog>> GetRecentAsync(int limit, CancellationToken ct = default)
        {
            return await _db.ProcessExecutionLogs
                .AsNoTracking()
                .OrderByDescending(l => l.Timestamp)
                .Take(limit)
                .ToListAsync(ct);
        }
    }
}
