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

namespace Listenarr.Application.Common
{
    /// <summary>
    /// Centralizes the non-fatal exception rule used by background workers.
    /// </summary>
    public static class WorkerExceptionClassifier
    {
        public static bool IsNonFatal(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                // HttpClient surfaces its own configured Timeout as a TaskCanceledException whose
                // InnerException is a TimeoutException. That is a failed request, not a caller or
                // shutdown cancellation, so it stays non-fatal and can be logged and absorbed.
                return exception.InnerException is TimeoutException;
            }

            return exception is not OutOfMemoryException
                && exception is not StackOverflowException;
        }
    }
}
