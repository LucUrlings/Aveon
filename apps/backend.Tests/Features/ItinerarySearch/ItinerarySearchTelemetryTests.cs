using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using backend.Features.ItinerarySearch;
using Xunit;

namespace backend.Tests;

public sealed class ItinerarySearchTelemetryTests
{
    [Fact]
    public void LifecycleMetrics_UseOnlyLowCardinalityNonSensitiveTags()
    {
        var measurements = new ConcurrentBag<(string Name, IReadOnlyList<KeyValuePair<string, object?>> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ItinerarySearchTelemetry.MeterName) meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => measurements.Add((instrument.Name, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => measurements.Add((instrument.Name, tags.ToArray())));
        listener.Start();

        ItinerarySearchTelemetry.RecordStarted("optimize");
        ItinerarySearchTelemetry.RecordValidationFailure("ordered");
        ItinerarySearchTelemetry.RecordFinished("optimize", "completed", "bounded", 12, 25, System.Diagnostics.Stopwatch.GetTimestamp());

        var snapshot = measurements.ToArray();
        Assert.Contains(snapshot, measurement => measurement.Name == "itinerary_search.started");
        Assert.Contains(snapshot, measurement => measurement.Name == "itinerary_search.finished");
        Assert.Contains(snapshot, measurement => measurement.Name == "itinerary_search.validation_failures");
        Assert.All(snapshot.SelectMany(measurement => measurement.Tags), tag =>
            Assert.Contains(tag.Key, new[] { "mode", "status", "coverage" }));
        Assert.DoesNotContain(snapshot.SelectMany(measurement => measurement.Tags), tag =>
            tag.Key.Contains("url", StringComparison.OrdinalIgnoreCase) ||
            tag.Key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            tag.Key.Contains("key", StringComparison.OrdinalIgnoreCase));
    }
}
