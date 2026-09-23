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
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Listenarr.Infrastructure.SystemDiagnostics.Diagnostics;

/// <summary>
/// Aggregates the <c>Listenarr.Backend</c> meter (see <see cref="MeterAppMetricsService"/>) in
/// memory: counters are summed, gauges keep their last observed value, and timings keep a
/// count/sum/min/max summary. There is no exporter, no external network call, and nothing
/// outside this process ever sees these values; a snapshot is only produced when
/// <see cref="GetSnapshot"/> is called, typically by the metrics read endpoint.
/// </summary>
/// <remarks>
/// Registered as an <see cref="IHostedService"/> as well as the DI singleton for
/// <see cref="IAppMetricsSnapshotProvider"/>, so the listener starts at application boot rather
/// than lazily on first request. A listener started late would silently miss every measurement
/// recorded before it attached; <see cref="MeterListener"/> has no replay buffer.
/// </remarks>
public sealed class MeterListenerAppMetricsSnapshotProvider
    : IAppMetricsSnapshotProvider, IHostedService, IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentDictionary<string, double> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, double> _gauges = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TimingAccumulator> _timings = new(StringComparer.Ordinal);

    public MeterListenerAppMetricsSnapshotProvider()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MeterAppMetricsService.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _listener.SetMeasurementEventCallback<double>(OnMeasurementRecorded);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener.Dispose();
        return Task.CompletedTask;
    }

    public MetricsSnapshot GetSnapshot()
    {
        // Observable gauges (see MeterAppMetricsService.Gauge) only report their current value
        // when a listener asks for it; this pulls the latest value from every gauge callback
        // immediately before building the snapshot.
        _listener.RecordObservableInstruments();

        return new MetricsSnapshot
        {
            CapturedAtUtc = DateTime.UtcNow,
            Counters = new Dictionary<string, double>(_counters, StringComparer.Ordinal),
            Gauges = new Dictionary<string, double>(_gauges, StringComparer.Ordinal),
            Timings = _timings.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToSummary(),
                StringComparer.Ordinal)
        };
    }

    public void Dispose() => _listener.Dispose();

    private void OnMeasurementRecorded(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        switch (instrument)
        {
            case Counter<double>:
                _counters.AddOrUpdate(
                    instrument.Name,
                    measurement,
                    (_, existing) => existing + measurement);
                break;
            case Histogram<double>:
                _timings.AddOrUpdate(
                    instrument.Name,
                    _ => TimingAccumulator.Start(measurement),
                    (_, existing) => existing.Record(measurement));
                break;
            case ObservableGauge<double>:
                _gauges[instrument.Name] = measurement;
                break;
        }
    }

    private readonly record struct TimingAccumulator(long Count, double Sum, double Min, double Max)
    {
        public static TimingAccumulator Start(double value) => new(1, value, value, value);

        public TimingAccumulator Record(double value) =>
            new(Count + 1, Sum + value, Math.Min(Min, value), Math.Max(Max, value));

        public MetricTimingSummary ToSummary() => new()
        {
            Count = Count,
            SumMs = Sum,
            MinMs = Min,
            MaxMs = Max
        };
    }
}
