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
    public class EfAudiobookFileRepository : IAudiobookFileRepository
    {
        private readonly ListenArrDbContext _db;

        public EfAudiobookFileRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<AudiobookFile?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await _db.AudiobookFiles.FindAsync(new object[] { id }, ct);
        }

        public async Task<List<AudiobookFile>> GetByAudiobookIdAsync(int audiobookId, CancellationToken ct = default)
        {
            return await _db.AudiobookFiles
                .AsNoTracking()
                .Where(f => f.AudiobookId == audiobookId)
                .ToListAsync(ct);
        }

        public async Task<List<AudiobookFile>> GetMissingMetadataAsync(int max, CancellationToken ct = default)
        {
            return await _db.AudiobookFiles
                .AsNoTracking()
                .Where(f => f.DurationSeconds == null || f.Format == null || f.SampleRate == null)
                .Take(max)
                .ToListAsync(ct);
        }

        public async Task<AudiobookFile> AddAsync(AudiobookFile file, CancellationToken ct = default)
        {
            _db.AudiobookFiles.Add(file);
            await _db.SaveChangesAsync(ct);
            return file;
        }

        public async Task UpdateAsync(AudiobookFile file, CancellationToken ct = default)
        {
            _db.AudiobookFiles.Update(file);
            await _db.SaveChangesAsync(ct);
        }

        public async Task DeleteByAudiobookIdAsync(int audiobookId, CancellationToken ct = default)
        {
            var files = await _db.AudiobookFiles.Where(f => f.AudiobookId == audiobookId).ToListAsync(ct);
            _db.AudiobookFiles.RemoveRange(files);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<bool> ExistsAtPathAsync(int audiobookId, string path, CancellationToken ct = default)
        {
            return await _db.AudiobookFiles.AnyAsync(f => f.AudiobookId == audiobookId && f.Path == path, ct);
        }

        public async Task<bool> IsPathUsedByOtherAsync(int audiobookId, string path, CancellationToken ct = default)
        {
            return await _db.AudiobookFiles.AnyAsync(f => f.AudiobookId != audiobookId && f.Path == path, ct);
        }
    }
}
