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

namespace Listenarr.Infrastructure.SystemDiagnostics.Diagnostics
{
    /// <summary>
    /// The outcome of one connectivity probe against one enabled download client,
    /// carrying only what the health payload needs so the mapper stays a pure function.
    /// </summary>
    internal readonly record struct DownloadClientProbe(string Name, string? Type, string Status);

    /// <summary>
    /// Per-client status values. These match the vocabulary the frontend already
    /// documents on ClientStatus in fe/src/types/index.ts.
    /// </summary>
    internal static class DownloadClientProbeStatuses
    {
        /// <summary>The client answered the probe.</summary>
        public const string Connected = "connected";

        /// <summary>The client was reached and refused, or could not be reached at all.</summary>
        public const string Disconnected = "disconnected";

        /// <summary>
        /// The probe could not produce an answer: it outran the health-endpoint budget,
        /// or no adapter is registered for the configured client type. Deliberately not
        /// reported as disconnected, because we did not observe the client being down.
        /// </summary>
        public const string Unknown = "unknown";
    }
}
