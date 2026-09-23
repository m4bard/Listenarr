/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    public class EfDownloadClientStatusRepository : IDownloadClientStatusRepository
    {
        private readonly ListenArrDbContext _db;

        public EfDownloadClientStatusRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<DownloadClientStatus>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.DownloadClientStatuses.AsNoTracking().ToListAsync(ct);
        }

        public async Task<DownloadClientStatus?> GetByClientIdAsync(string clientId, CancellationToken ct = default)
        {
            return await _db.DownloadClientStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.ClientId == clientId, ct);
        }

        public async Task<bool> UpsertAsync(DownloadClientStatus status, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(status);

            var existing = await _db.DownloadClientStatuses.FirstOrDefaultAsync(s => s.ClientId == status.ClientId, ct);
            if (existing == null)
            {
                // Checked rather than left to the foreign key, because the in-memory provider the
                // tests use does not enforce one and a connection test for an unsaved client is an
                // ordinary event, not an error worth an exception.
                var clientExists = await _db.DownloadClientConfigurations.AsNoTracking().AnyAsync(c => c.Id == status.ClientId, ct);
                if (!clientExists)
                {
                    return false;
                }

                _db.DownloadClientStatuses.Add(status);
            }
            else if (!ReferenceEquals(existing, status))
            {
                _db.Entry(existing).CurrentValues.SetValues(status);
            }

            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
