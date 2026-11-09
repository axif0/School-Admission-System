using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;

namespace SchoolAdmission.Api.Observability;

public class AppMetrics : IDisposable
{
    public const string MeterName = "SchoolAdmission";
    private readonly Meter _meter;
    private readonly MeterListener _listener;

    private readonly ConcurrentDictionary<string, double> _counters = new();
    private readonly ConcurrentDictionary<string, HistogramTracker> _histograms = new();

    public Counter<long> HoldsCreated { get; }
    public Counter<long> HoldsRejected { get; }
    public Counter<long> ConfirmsTotal { get; }
    public Counter<long> CancelsTotal { get; }
    public Counter<long> ExpiredSweptTotal { get; }
    public Histogram<double> HoldDurationSeconds { get; }
    public Histogram<double> ClassReadLatencySeconds { get; }

    public AppMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        HoldsCreated = _meter.CreateCounter<long>("holds_created_total", description: "Total holds successfully created");
        HoldsRejected = _meter.CreateCounter<long>("holds_rejected_total", description: "Total holds rejected");
        ConfirmsTotal = _meter.CreateCounter<long>("confirms_total", description: "Total hold confirmation attempts");
        CancelsTotal = _meter.CreateCounter<long>("cancels_total", description: "Total holds cancelled");
        ExpiredSweptTotal = _meter.CreateCounter<long>("expired_swept_total", description: "Total expired holds swept");
        HoldDurationSeconds = _meter.CreateHistogram<double>("hold_duration_seconds", unit: "s", description: "Hold lifespan before confirmation or expiry");
        ClassReadLatencySeconds = _meter.CreateHistogram<double>("class_read_latency_seconds", unit: "s", description: "Latency of class queries");

        _histograms.GetOrAdd("hold_duration_seconds", _ => new HistogramTracker());
        _histograms.GetOrAdd("class_read_latency_seconds", _ => new HistogramTracker());

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>(OnMeasurementRecordedLong);
        _listener.SetMeasurementEventCallback<double>(OnMeasurementRecordedDouble);
        _listener.Start();
    }

    private void OnMeasurementRecordedLong(Instrument instrument, long measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        RecordMetric(instrument.Name, measurement, tags);
    }

    private void OnMeasurementRecordedDouble(Instrument instrument, double measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (instrument.Name is "hold_duration_seconds" or "class_read_latency_seconds")
        {
            _histograms.GetOrAdd(instrument.Name, _ => new HistogramTracker()).Record(measurement);
        }
        else
        {
            RecordMetric(instrument.Name, measurement, tags);
        }
    }

    private void RecordMetric(string name, double measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var tagString = FormatTags(tags);
        var key = string.IsNullOrEmpty(tagString) ? name : $"{name}{{{tagString}}}";
        _counters.AddOrUpdate(key, measurement, (_, prev) => prev + measurement);
    }

    private static string FormatTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (tags.IsEmpty) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < tags.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(tags[i].Key).Append("=\"").Append(tags[i].Value).Append('"');
        }
        return sb.ToString();
    }

    public string ToPrometheusFormat(bool openMetrics = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# HELP holds_created_total Total holds successfully created");
        sb.AppendLine("# TYPE holds_created_total counter");
        sb.AppendLine($"holds_created_total {_counters.GetValueOrDefault("holds_created_total", 0)}");

        sb.AppendLine("# HELP holds_rejected_total Total holds rejected");
        sb.AppendLine("# TYPE holds_rejected_total counter");
        foreach (var reason in new[] { "SEATS_FULL", "ACTIVE_HOLD_EXISTS", "VALIDATION" })
        {
            var key = $"holds_rejected_total{{reason=\"{reason}\"}}";
            sb.AppendLine($"{key} {_counters.GetValueOrDefault(key, 0)}");
        }

        sb.AppendLine("# HELP confirms_total Total hold confirmation attempts");
        sb.AppendLine("# TYPE confirms_total counter");
        foreach (var result in new[] { "CONFIRMED", "ALREADY_CONFIRMED" })
        {
            var key = $"confirms_total{{result=\"{result}\"}}";
            sb.AppendLine($"{key} {_counters.GetValueOrDefault(key, 0)}");
        }

        sb.AppendLine("# HELP cancels_total Total holds cancelled");
        sb.AppendLine("# TYPE cancels_total counter");
        sb.AppendLine($"cancels_total {_counters.GetValueOrDefault("cancels_total", 0)}");

        sb.AppendLine("# HELP expired_swept_total Total expired holds swept");
        sb.AppendLine("# TYPE expired_swept_total counter");
        sb.AppendLine($"expired_swept_total {_counters.GetValueOrDefault("expired_swept_total", 0)}");

        if (_histograms.TryGetValue("hold_duration_seconds", out var holdHist))
        {
            holdHist.FormatPrometheus("hold_duration_seconds", "Hold lifespan before confirmation or expiry", sb);
        }

        if (_histograms.TryGetValue("class_read_latency_seconds", out var classHist))
        {
            classHist.FormatPrometheus("class_read_latency_seconds", "Latency of class queries", sb);
        }

        if (openMetrics)
        {
            sb.AppendLine("# EOF");
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meter.Dispose();
    }

    private class HistogramTracker
    {
        private static readonly (double Value, string Label)[] Buckets = new[]
        {
            (0.005, "0.005"),
            (0.01,  "0.01"),
            (0.025, "0.025"),
            (0.05,  "0.05"),
            (0.1,   "0.1"),
            (0.25,  "0.25"),
            (0.5,   "0.5"),
            (1.0,   "1"),
            (2.5,   "2.5"),
            (5.0,   "5"),
            (10.0,  "10")
        };

        private readonly long[] _bucketCounts = new long[Buckets.Length];
        private long _count;
        private double _sum;
        private readonly object _lock = new();

        public void Record(double value)
        {
            lock (_lock)
            {
                _count++;
                _sum += value;
                for (int i = 0; i < Buckets.Length; i++)
                {
                    if (value <= Buckets[i].Value)
                    {
                        _bucketCounts[i]++;
                    }
                }
            }
        }

        public void FormatPrometheus(string metricName, string description, StringBuilder sb)
        {
            sb.AppendLine($"# HELP {metricName} {description}");
            sb.AppendLine($"# TYPE {metricName} histogram");

            long totalCount;
            double totalSum;
            long[] snapshot = new long[Buckets.Length];

            lock (_lock)
            {
                totalCount = _count;
                totalSum = _sum;
                Array.Copy(_bucketCounts, snapshot, _bucketCounts.Length);
            }

            for (int i = 0; i < Buckets.Length; i++)
            {
                sb.AppendLine($"{metricName}_bucket{{le=\"{Buckets[i].Label}\"}} {snapshot[i]}");
            }
            sb.AppendLine($"{metricName}_bucket{{le=\"+Inf\"}} {totalCount}");
            sb.AppendLine($"{metricName}_sum {totalSum.ToString("0.######", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"{metricName}_count {totalCount}");
        }
    }
}
