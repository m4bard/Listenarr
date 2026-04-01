using System.Collections.Concurrent;
using System.Threading;

namespace Listenarr.Api.Services
{
    public interface ILoginRateLimiter
    {
        bool IsBlocked(string key);
        void RecordFailure(string key);
        void RecordSuccess(string key);
        /// <summary>
        /// If the key is blocked, returns remaining block duration in seconds; otherwise 0.
        /// </summary>
        int GetSecondsUntilUnblock(string key);
    }

    public class LoginRateLimiter : ILoginRateLimiter
    {
        private class Entry
        {
            // Failures is incremented atomically via Interlocked; BlockUntil is
            // written inside a lock on the Entry instance to avoid TOCTOU races.
            public int Failures;
            public DateTime? BlockUntil;
        }

        private readonly ConcurrentDictionary<string, Entry> _map = new();

        // Configurable thresholds
        private readonly int _maxFailures = 5;
        private readonly TimeSpan _blockDuration = TimeSpan.FromMinutes(10);

        public bool IsBlocked(string key)
        {
            if (_map.TryGetValue(key, out var e))
            {
                lock (e)
                {
                    if (e.BlockUntil.HasValue && e.BlockUntil.Value > DateTime.UtcNow) return true;
                }
            }
            return false;
        }

        public int GetSecondsUntilUnblock(string key)
        {
            if (_map.TryGetValue(key, out var e) && e.BlockUntil.HasValue)
            {
                lock (e)
                {
                    if (e.BlockUntil.HasValue)
                    {
                        var ts = e.BlockUntil.Value - DateTime.UtcNow;
                        return ts.Ticks > 0 ? (int)Math.Ceiling(ts.TotalSeconds) : 0;
                    }
                }
            }
            return 0;
        }

        public void RecordFailure(string key)
        {
            var entry = _map.GetOrAdd(key, _ => new Entry());
            // Atomically increment and then decide whether to set BlockUntil under the same lock
            // so that concurrent callers cannot both observe Failures < _maxFailures and skip blocking.
            lock (entry)
            {
                entry.Failures++;
                if (entry.Failures >= _maxFailures)
                {
                    entry.BlockUntil = DateTime.UtcNow.Add(_blockDuration);
                }
            }
        }

        public void RecordSuccess(string key)
        {
            _map.TryRemove(key, out _);
        }
    }
}
