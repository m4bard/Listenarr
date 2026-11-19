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

namespace Listenarr.Infrastructure.HostedServices.Search
{
    // A debug broadcast must never decide whether a grab happens. Contain every failure,
    // including an OperationCanceledException that is not the host stopping (a timeout
    // surfaces as TaskCanceledException). A real shutdown still propagates.
    internal static class AutomaticSearchBroadcastFailure
    {
        public static bool IsContainable(Exception ex, CancellationToken stoppingToken) =>
            ex is not OutOfMemoryException
            && ex is not StackOverflowException
            && !(ex is OperationCanceledException && stoppingToken.IsCancellationRequested);
    }
}
