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

using System.Diagnostics.Metrics;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.SystemDiagnostics.Diagnostics
{
    [Trait("Area", "Infrastructure")]
    [Trait("Name", "MeterListenerAppMetricsSnapshotProviderTests")]
    [Trait("Category", "MetricsSnapshot")]
    public class MeterListenerAppMetricsSnapshotProviderTests : BaseTests
    {
        [Fact]
        [Trait("Method", "GetSnapshot")]
        [Trait("Scenario", "AggregatesMeasurementsRecordedThroughAppMetricsService")]
        public async Task GetSnapshot_AggregatesMeasurementsRecordedThroughAppMetricsService()
        {
            // Given: the snapshot provider is listening, and unique instrument names so this
            // test cannot collide with another test recording to the same meter in parallel.
            var suffix = Guid.NewGuid().ToString("N");
            var counterName = $"test.counter.{suffix}";
            var gaugeName = $"test.gauge.{suffix}";
            var timingName = $"test.duration.{suffix}";

            using var provider = new MeterListenerAppMetricsSnapshotProvider();
            await provider.StartAsync(CancellationToken.None);
            using var appMetrics = new MeterAppMetricsService();

            // When: recorded through the same contract production code uses
            appMetrics.Increment(counterName, 2);
            appMetrics.Increment(counterName, 3);
            appMetrics.Gauge(gaugeName, 42);
            appMetrics.Timing(timingName, TimeSpan.FromMilliseconds(10));
            appMetrics.Timing(timingName, TimeSpan.FromMilliseconds(30));

            var snapshot = provider.GetSnapshot();

            // Then
            Assert.Equal(5, snapshot.Counters[counterName]);
            Assert.Equal(42, snapshot.Gauges[gaugeName]);
            var timing = snapshot.Timings[timingName];
            Assert.Equal(2, timing.Count);
            Assert.Equal(40, timing.SumMs);
            Assert.Equal(10, timing.MinMs);
            Assert.Equal(30, timing.MaxMs);

            await provider.StopAsync(CancellationToken.None);
        }

        [Fact]
        [Trait("Method", "GetSnapshot")]
        [Trait("Scenario", "IgnoresInstrumentsFromADifferentMeter")]
        public async Task GetSnapshot_IgnoresInstrumentsFromADifferentMeter()
        {
            // Given: a control. An instrument published on a meter with a different name from
            // MeterAppMetricsService.MeterName must never reach the snapshot, proving the
            // provider filters by meter name rather than aggregating everything in the process.
            var suffix = Guid.NewGuid().ToString("N");
            var counterName = $"other.counter.{suffix}";

            using var provider = new MeterListenerAppMetricsSnapshotProvider();
            await provider.StartAsync(CancellationToken.None);
            using var unrelatedMeter = new Meter($"Some.Unrelated.Meter.{suffix}");
            var unrelatedCounter = unrelatedMeter.CreateCounter<double>(counterName);

            // When
            unrelatedCounter.Add(99);
            var snapshot = provider.GetSnapshot();

            // Then
            Assert.DoesNotContain(counterName, snapshot.Counters.Keys);

            await provider.StopAsync(CancellationToken.None);
        }
    }
}
