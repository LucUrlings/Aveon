using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace backend.Features.ItinerarySearch;

public static class ItinerarySearchTelemetry
{
    public const string MeterName = "Aveon.ItinerarySearch";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> SearchesStarted = Meter.CreateCounter<long>("itinerary_search.started");
    private static readonly Counter<long> SearchesFinished = Meter.CreateCounter<long>("itinerary_search.finished");
    private static readonly Counter<long> SearchesCanceled = Meter.CreateCounter<long>("itinerary_search.canceled");
    private static readonly Counter<long> ValidationFailures = Meter.CreateCounter<long>("itinerary_search.validation_failures");
    private static readonly Histogram<double> ExecutionDuration = Meter.CreateHistogram<double>("itinerary_search.duration", "s");
    private static readonly Histogram<long> ResultsReturned = Meter.CreateHistogram<long>("itinerary_search.results");
    private static readonly Histogram<long> LiveProviderCalls = Meter.CreateHistogram<long>("itinerary_search.live_provider_calls");
    private static long _activeSearches;

    static ItinerarySearchTelemetry() => Meter.CreateObservableGauge(
        "itinerary_search.active",
        () => Interlocked.Read(ref _activeSearches));

    public static void RecordStarted(string mode)
    {
        Interlocked.Increment(ref _activeSearches);
        SearchesStarted.Add(1, new KeyValuePair<string, object?>("mode", mode));
    }

    public static void RecordValidationFailure(string mode) =>
        ValidationFailures.Add(1, new KeyValuePair<string, object?>("mode", mode));

    public static void RecordCanceled(string mode)
    {
        DecrementActiveSearches();
        SearchesCanceled.Add(1, new KeyValuePair<string, object?>("mode", mode));
    }

    public static void RecordFinished(string mode, string status, string coverage, int resultCount, int liveProviderCalls, long startedTimestamp)
    {
        DecrementActiveSearches();
        var tags = new TagList
        {
            { "mode", mode },
            { "status", status },
            { "coverage", coverage }
        };
        SearchesFinished.Add(1, tags);
        ExecutionDuration.Record(Stopwatch.GetElapsedTime(startedTimestamp).TotalSeconds, tags);
        ResultsReturned.Record(resultCount, tags);
        LiveProviderCalls.Record(liveProviderCalls, tags);
    }

    private static void DecrementActiveSearches()
    {
        while (true)
        {
            var current = Interlocked.Read(ref _activeSearches);
            if (current == 0 || Interlocked.CompareExchange(ref _activeSearches, current - 1, current) == current) return;
        }
    }
}
