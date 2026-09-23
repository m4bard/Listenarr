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

namespace Listenarr.Domain.SystemDiagnostics
{
    /// <summary>
    /// A point-in-time aggregation of everything recorded through <c>IAppMetricsService</c>,
    /// returned by <c>GET /system/metrics</c>. Counters are cumulative sums since process start,
    /// gauges are the last observed value, and timings are a count/sum/min/max summary in
    /// milliseconds rather than every individual sample.
    /// </summary>
    public class MetricsSnapshot
    {
        public DateTime CapturedAtUtc { get; set; }
        public IReadOnlyDictionary<string, double> Counters { get; set; } = new Dictionary<string, double>();
        public IReadOnlyDictionary<string, double> Gauges { get; set; } = new Dictionary<string, double>();
        public IReadOnlyDictionary<string, MetricTimingSummary> Timings { get; set; } =
            new Dictionary<string, MetricTimingSummary>();
    }

    /// <summary>
    /// Aggregated view of a single timing instrument's recorded samples, in milliseconds.
    /// </summary>
    public class MetricTimingSummary
    {
        public long Count { get; set; }
        public double SumMs { get; set; }
        public double MinMs { get; set; }
        public double MaxMs { get; set; }
    }
}
