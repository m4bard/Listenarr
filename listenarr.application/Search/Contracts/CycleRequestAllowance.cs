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

namespace Listenarr.Application.Search.Contracts
{
    /// <summary>
    /// The outbound request allowance of one worker cycle. Thread-safe. A unit that was taken is
    /// not returned if the request is later deferred or fails.
    /// </summary>
    public sealed class CycleRequestAllowance
    {
        private readonly int _maxRequests;
        private int _taken;

        /// <summary>
        /// Creates the allowance for one cycle.
        /// </summary>
        /// <param name="maxRequests">How many requests the cycle may send. Zero allows none.</param>
        public CycleRequestAllowance(int maxRequests)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maxRequests);
            _maxRequests = maxRequests;
        }

        /// <summary>
        /// How many units have been taken so far. Never exceeds the maximum.
        /// </summary>
        public int Taken => Volatile.Read(ref _taken);

        /// <summary>
        /// Takes one unit for one request on the wire.
        /// </summary>
        /// <returns>True if a unit was taken; false once the maximum has been taken.</returns>
        public bool TryTake()
        {
            while (true)
            {
                var taken = Volatile.Read(ref _taken);
                if (taken >= _maxRequests)
                {
                    return false;
                }

                // Compare-and-swap so concurrent callers can never take more than the maximum.
                if (Interlocked.CompareExchange(ref _taken, taken + 1, taken) == taken)
                {
                    return true;
                }
            }
        }
    }
}
