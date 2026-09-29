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
    public class EfIndexerRepository : IIndexerRepository
    {
        private readonly ListenArrDbContext _db;
        private readonly IDbContextFactory<ListenArrDbContext>? _dbFactory;

        /// <param name="db">The scope's context, used by every method except the backoff write.</param>
        /// <param name="dbFactory">
        /// Source of a context of its own for <see cref="UpdateBackoffStateAsync"/>. Always supplied
        /// by DI, since the scoped context itself is registered through AddDbContextFactory. Optional
        /// only so tests that construct the repository by hand over a single context keep working;
        /// without it the backoff write shares <paramref name="db"/> and must not be called
        /// concurrently.
        /// </param>
        public EfIndexerRepository(ListenArrDbContext db, IDbContextFactory<ListenArrDbContext>? dbFactory = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _dbFactory = dbFactory;
        }

        public async Task<Indexer?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await _db.Indexers.FindAsync(new object[] { id }, ct);
        }

        public async Task<Indexer?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            return await _db.Indexers.AsNoTracking().FirstOrDefaultAsync(i => i.Name == name, ct);
        }

        public async Task<List<Indexer>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.Indexers.AsNoTracking().ToListAsync(ct);
        }

        public async Task<List<Indexer>> GetEnabledAsync(bool isAutomaticSearch, CancellationToken ct = default)
        {
            return await _db.Indexers
                .AsNoTracking()
                .Where(i => i.IsEnabled && (isAutomaticSearch ? i.EnableAutomaticSearch : i.EnableInteractiveSearch))
                .OrderBy(i => i.Priority)
                .ToListAsync(ct);
        }

        public async Task<Indexer> AddAsync(Indexer indexer, CancellationToken ct = default)
        {
            _db.Indexers.Add(indexer);
            await _db.SaveChangesAsync(ct);
            return indexer;
        }

        public async Task UpdateAsync(Indexer indexer, CancellationToken ct = default)
        {
            var existing = await _db.Indexers.FindAsync(new object[] { indexer.Id }, ct);
            if (existing == null) throw new InvalidOperationException($"Indexer {indexer.Id} not found.");

            // The failure-backoff columns are not operator-editable and are written only by
            // UpdateBackoffStateAsync. SetValues below is a whole-row overwrite, so without this
            // they would be carried back from whatever the caller read -- for a settings form,
            // whatever they stood at when the form was opened. Saving unrelated settings during a
            // cooldown would then silently lift it, and the indexer would be asked again
            // immediately for no reason anyone could see.
            var backoff = IndexerBackoffState.From(existing);

            _db.Entry(existing).CurrentValues.SetValues(indexer);

            existing.InitialFailure = backoff.InitialFailure;
            existing.MostRecentFailure = backoff.MostRecentFailure;
            existing.EscalationLevel = backoff.EscalationLevel;
            existing.DisabledTill = backoff.DisabledTill;
            existing.LastFailureReason = backoff.LastFailureReason;

            await _db.SaveChangesAsync(ct);
        }

        public async Task UpdateBackoffStateAsync(int indexerId, IndexerBackoffState state, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(state);

            // A context of its own, not the scope's. This is called from inside the indexer search
            // fan-out, once per indexer whose rung changed, and every indexer in that search shares
            // this repository and its scoped context. EF rejects a second operation on a context
            // before the first completes, so two indexers failing in the same batch would otherwise
            // lose one of the two writes.
            if (_dbFactory == null)
            {
                await WriteBackoffStateAsync(_db, indexerId, state, ct);
                return;
            }

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            await WriteBackoffStateAsync(db, indexerId, state, ct);
        }

        private static Task<int> WriteBackoffStateAsync(
            ListenArrDbContext db,
            int indexerId,
            IndexerBackoffState state,
            CancellationToken ct) =>
            // Named columns only. The whole-entity SetValues in UpdateAsync would carry back every
            // configuration column as it stood when the caller read the row, which for a status
            // writer is always a stale read.
            db.Indexers
                .Where(i => i.Id == indexerId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(i => i.InitialFailure, state.InitialFailure)
                        .SetProperty(i => i.MostRecentFailure, state.MostRecentFailure)
                        .SetProperty(i => i.EscalationLevel, state.EscalationLevel)
                        .SetProperty(i => i.DisabledTill, state.DisabledTill)
                        .SetProperty(i => i.LastFailureReason, state.LastFailureReason),
                    ct);

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            var indexer = await _db.Indexers.FindAsync(new object[] { id }, ct);
            if (indexer == null) return;
            _db.Indexers.Remove(indexer);
            await _db.SaveChangesAsync(ct);
        }
    }
}
