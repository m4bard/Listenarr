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
using System.Threading.Channels;

namespace Listenarr.Api.Services
{
    /// <summary>
    /// Lightweight in-memory channel to publish newly queued download processing job IDs
    /// for in-memory consumers to react to immediately (best-effort; DB remains source of truth).
    /// </summary>
    public class DownloadProcessingChannel : IProcessingChannel
    {
        private readonly Channel<string> _channel;

        public DownloadProcessingChannel()
        {
            // Unbounded channel: callers expect enqueue to succeed without blocking. Use single-writer/multi-reader semantics.
            _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        }

        public async ValueTask EnqueueJobAsync(string jobId, CancellationToken ct = default)
        {
            await _channel.Writer.WriteAsync(jobId, ct).ConfigureAwait(false);
        }

        public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct = default)
        {
            return _channel.Reader.ReadAllAsync(ct);
        }

        public bool TryWrite(string jobId) => _channel.Writer.TryWrite(jobId);
    }
}
