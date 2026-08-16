using backend.Features.Explore;
using backend.Features.Explore.Models;
using backend.Infrastructure.Airports;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace backend.Tests;

public sealed class ExploreRouteServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FetchesEveryPage_DeduplicatesCodeshares_AndCachesCompleteNetwork()
    {
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = Page(1, 2, Destination("AMS", "Amsterdam Schiphol", "Amsterdam", "Netherlands", 52.31, 4.76), Destination("AMS", "Amsterdam Schiphol", "Amsterdam", "Netherlands", 52.31, 4.76)),
            [2] = Page(2, 2, Destination("JFK", "John F. Kennedy International", "New York", "United States", 40.64, -73.78))
        });
        var cache = new FixtureExploreCache();
        var service = CreateService(provider, cache);

        var response = await service.GetRoutesAsync("dub", ExploreCacheProfile.Explore, CancellationToken.None);

        Assert.Equal([1, 2], provider.Pages);
        Assert.Equal("DUB", response.Origin.Code);
        Assert.Equal(["AMS", "JFK"], response.Destinations.Select(destination => destination.Code).ToList());
        Assert.True(response.IsComplete);
        Assert.False(response.IsStale);
        Assert.Equal(new DateOnly(2026, 7, 28), response.ObservedFrom);
        Assert.Equal(1, cache.SetCount);
    }

    [Fact]
    public async Task FreshCache_AvoidsFlightApi()
    {
        var cached = Schedule(Now.AddDays(-1));
        var cache = new FixtureExploreCache(cached);
        var provider = new FixtureScheduleProvider([]);
        var logger = new RecordingLogger<ExploreRouteService>();
        var service = CreateService(provider, cache, logger: logger);

        var response = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None);

        Assert.Empty(provider.Pages);
        Assert.False(response.IsStale);
        Assert.Contains(logger.Messages, message => message.Contains("cache hit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logger.Messages, message => message.Contains("no live FlightAPI", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CacheMiss_LogsThatLiveScheduleRefreshIsRequired()
    {
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = PageFor("DXB", 1, 1)
        });
        var logger = new RecordingLogger<ExploreRouteService>();
        var service = CreateService(provider, new FixtureExploreCache(), logger: logger);

        await service.GetRoutesAsync("DXB", ExploreCacheProfile.Explore, CancellationToken.None);

        Assert.Contains(logger.Messages, message => message.Contains("cache miss", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logger.Messages, message => message.Contains("live FlightAPI schedules", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedRefresh_ServesRetainedStaleNetwork()
    {
        var cached = Schedule(Now.AddDays(-8));
        var cache = new FixtureExploreCache(cached);
        var provider = new FixtureScheduleProvider([], new HttpRequestException("provider unavailable"));
        var service = CreateService(provider, cache);

        var response = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None);

        Assert.True(response.IsStale);
        Assert.Single(provider.Pages);
        Assert.Equal(0, cache.SetCount);
    }

    [Theory]
    [MemberData(nameof(UncachedProviderFailures))]
    public async Task CacheMiss_RecognizedProviderFailureBecomesSafeExploreFailure(Exception providerFailure)
    {
        var provider = new FixtureScheduleProvider([], providerFailure);
        var service = CreateService(provider, new FixtureExploreCache());

        var exception = await Assert.ThrowsAsync<ExploreProviderUnavailableException>(() =>
            service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None));

        Assert.Same(providerFailure, exception.InnerException);
    }

    public static TheoryData<Exception> UncachedProviderFailures => new()
    {
        new HttpRequestException("network unavailable"),
        new JsonException("malformed provider payload"),
        new TimeoutException("provider timed out"),
        new OperationCanceledException("provider timeout without caller cancellation")
    };

    [Fact]
    public async Task CallerCancellation_IsNotConvertedToProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var providerFailure = new OperationCanceledException(cancellation.Token);
        var provider = new FixtureScheduleProvider([], providerFailure);
        var service = CreateService(provider, new FixtureExploreCache());

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Empty(provider.Pages);
        Assert.Empty(provider.V2Pages);
    }

    [Fact]
    public async Task MonthOldHeroProfile_RemainsFreshLongerThanExploreProfile()
    {
        var cached = Schedule(Now.AddDays(-8));
        var cache = new FixtureExploreCache(cached);
        var provider = new FixtureScheduleProvider([]);
        var service = CreateService(provider, cache);

        var response = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Hero, CancellationToken.None);

        Assert.Empty(provider.Pages);
        Assert.False(response.IsStale);
    }

    [Fact]
    public async Task ProviderPaginationAboveSafetyLimit_IsMarkedPartialAndNotCached()
    {
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = Page(1, 4, Destination("AMS", "Amsterdam", "Amsterdam", "Netherlands", 52.31, 4.76))
        });
        var cache = new FixtureExploreCache();
        var service = CreateService(provider, cache, maxPages: 1);

        var response = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None);

        Assert.False(response.IsComplete);
        Assert.Equal(0, cache.SetCount);
    }

    [Fact]
    public async Task EmptySchedule_ReturnsAndCachesACompleteEmptyNetwork()
    {
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = Page(1, 1)
        });
        var cache = new FixtureExploreCache();
        var service = CreateService(provider, cache);

        var response = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None);

        Assert.Empty(response.Destinations);
        Assert.True(response.IsComplete);
        Assert.Equal(1, cache.SetCount);
    }

    [Fact]
    public async Task RollingSchedule_MissingPageOneEnvelopeIsAProviderFailureAndIsNotCached()
    {
        var cache = new FixtureExploreCache();
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = new()
        });
        var service = CreateService(provider, cache);

        await Assert.ThrowsAsync<ExploreProviderUnavailableException>(() =>
            service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None));

        Assert.Equal(0, cache.SetCount);
    }

    [Fact]
    public async Task ExactDate_UsesV2AndCatalogueWithoutCallingV1ForMetadata()
    {
        var provider = new FixtureScheduleProvider(
            new Dictionary<int, FlightApiScheduleResponse>
            {
                [1] = Page(1, 1,
                    Destination("AMS", "Amsterdam Schiphol", "Amsterdam", "Netherlands", 52.31, 4.76),
                    Destination("JFK", "John F. Kennedy International", "New York", "United States", 40.64, -73.78))
            },
            datedResponses: new Dictionary<int, FlightApiScheduleV2Response>
            {
                [1] = V2Page("AMS"),
                [2] = V2Page(),
                [3] = V2Page(),
                [4] = V2Page()
            });
        var cache = new FixtureExploreCache();
        var service = CreateService(provider, cache, maxPages: 4);
        var departureDate = new DateOnly(2026, 9, 18);

        var response = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None, departureDate);

        Assert.Equal("AMS", Assert.Single(response.Destinations).Code);
        Assert.Equal(departureDate, response.ObservedFrom);
        Assert.Equal(departureDate, response.ObservedTo);
        Assert.Equal([1, 2, 3, 4], provider.V2Pages);
        Assert.Empty(provider.Pages);
        Assert.True(response.IsComplete);
    }

    [Fact]
    public async Task ExactDate_WrongPageOneOriginIsAProviderFailureAndIsNotCached()
    {
        var cache = new FixtureExploreCache();
        var provider = new FixtureScheduleProvider(
            [],
            datedResponses: new Dictionary<int, FlightApiScheduleV2Response>
            {
                [1] = V2Page([], "AMS"),
                [2] = V2Page(),
                [3] = V2Page(),
                [4] = V2Page()
            });
        var service = CreateService(provider, cache, maxPages: 4);

        await Assert.ThrowsAsync<ExploreProviderUnavailableException>(() =>
            service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None, new(2026, 9, 18)));

        Assert.Equal(0, cache.SetCount);
    }

    [Fact]
    public async Task ExactDate_MalformedLaterPageReturnsPartialNetworkAndIsNotCached()
    {
        var cache = new FixtureExploreCache();
        var provider = new FixtureScheduleProvider(
            [],
            datedResponses: new Dictionary<int, FlightApiScheduleV2Response>
            {
                [1] = V2Page("AMS"),
                [2] = new() { Data = new() },
                [3] = V2Page(),
                [4] = V2Page()
            });
        var service = CreateService(provider, cache, maxPages: 4);

        var response = await service.GetRoutesAsync(
            "DUB",
            ExploreCacheProfile.Explore,
            CancellationToken.None,
            new(2026, 9, 18));

        Assert.Equal("AMS", Assert.Single(response.Destinations).Code);
        Assert.False(response.IsComplete);
        Assert.Equal(0, cache.SetCount);
    }

    [Fact]
    public async Task MissingCatalogueDestination_IsOmittedAndMarksResponseIncomplete()
    {
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = Page(1, 1, Destination("ZZZ", "Unknown", "Unknown", "", 1, 1))
        });
        var service = CreateService(provider, new FixtureExploreCache());

        var response = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None);

        Assert.Empty(response.Destinations);
        Assert.False(response.IsComplete);
    }

    [Fact]
    public async Task CachedSchedule_IsEnrichedFromLatestCatalogueOnEveryResponse()
    {
        var catalog = new FixtureAirportCatalog();
        var cache = new FixtureExploreCache(Schedule(Now.AddDays(-1)));
        var service = CreateService(new FixtureScheduleProvider([]), cache, catalog: catalog);

        var first = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None);
        catalog.SetAirportName("AMS", "Updated Schiphol name");
        var second = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None);

        Assert.Equal("Amsterdam Schiphol", Assert.Single(first.Destinations).Name);
        Assert.Equal("Updated Schiphol name", Assert.Single(second.Destinations).Name);
    }

    [Fact]
    public async Task LaterPageFailure_ReturnsPartialNetworkAndDoesNotCacheIt()
    {
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = Page(1, 2, Destination("AMS", "Amsterdam", "Amsterdam", "Netherlands", 52.31, 4.76))
        }, pageFailures: new Dictionary<int, Exception> { [2] = new HttpRequestException("page 2 unavailable") });
        var cache = new FixtureExploreCache();
        var service = CreateService(provider, cache);

        var response = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None);

        Assert.False(response.IsComplete);
        Assert.Equal("AMS", Assert.Single(response.Destinations).Code);
        Assert.Equal(0, cache.SetCount);
    }

    [Fact]
    public async Task PartialRefresh_ServesRetainedCompleteNetworkAsStale()
    {
        var cached = Schedule(Now.AddDays(-8));
        var cache = new FixtureExploreCache(cached);
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = Page(1, 2, Destination("JFK", "New York JFK", "New York", "United States", 40.64, -73.78))
        }, pageFailures: new Dictionary<int, Exception> { [2] = new HttpRequestException("page 2 unavailable") });
        var service = CreateService(provider, cache);

        var response = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None);

        Assert.True(response.IsComplete);
        Assert.True(response.IsStale);
        Assert.Equal("AMS", Assert.Single(response.Destinations).Code);
        Assert.Equal(0, cache.SetCount);
    }

    [Fact]
    public async Task IdenticalCacheMisses_ShareOneScheduleRefresh()
    {
        var provider = new BlockingScheduleProvider(Page(1, 1, Destination("AMS", "Amsterdam", "Amsterdam", "Netherlands", 52.31, 4.76)));
        var cache = new FixtureExploreCache();
        var service = CreateService(provider, cache);

        var first = service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var second = service.GetRoutesAsync("dub", ExploreCacheProfile.Explore, CancellationToken.None);
        provider.Release.TrySetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(2, results.Length);
        Assert.All(results, result => Assert.Single(result.Destinations));
    }

    [Fact]
    public void CacheRetention_UsesSeparateConfiguredProfilesAndClampsInvalidValues()
    {
        var options = new RedisOptions { ExploreRoutesRetentionMinutes = 123, HeroRoutesRetentionMinutes = 456 };

        Assert.Equal(123, RedisExploreRouteCache.RetentionMinutes(ExploreCacheProfile.Explore, options));
        Assert.Equal(456, RedisExploreRouteCache.RetentionMinutes(ExploreCacheProfile.Hero, options));
        Assert.Equal(1, RedisExploreRouteCache.RetentionMinutes(ExploreCacheProfile.Explore, options with { ExploreRoutesRetentionMinutes = 0 }));
    }

    [Fact]
    public void CacheKeys_IsolateRollingAndExactDateNetworks()
    {
        Assert.Equal("explore:routes:v2:explore:DUB:rolling", RedisExploreRouteCache.BuildKey(" dub ", ExploreCacheProfile.Explore, null));
        Assert.Equal("explore:routes:v2:explore:DUB:2026-09-18", RedisExploreRouteCache.BuildKey("DUB", ExploreCacheProfile.Explore, new DateOnly(2026, 9, 18)));
        Assert.NotEqual(
            RedisExploreRouteCache.BuildKey("DUB", ExploreCacheProfile.Explore, new DateOnly(2026, 9, 18)),
            RedisExploreRouteCache.BuildKey("DUB", ExploreCacheProfile.Explore, new DateOnly(2026, 9, 19)));
    }

    [Fact]
    public void ScheduleCachePayload_ContainsCodesAndObservationMetadataOnly()
    {
        var json = JsonSerializer.Serialize(Schedule(Now));

        Assert.Contains("\"DestinationCodes\":[\"AMS\"]", json);
        Assert.DoesNotContain("Amsterdam Schiphol", json);
        Assert.DoesNotContain("Latitude", json);
        Assert.DoesNotContain("Longitude", json);
    }

    [Fact]
    public void HeroSelection_UsesEveryCuratedHubAndNoOthers()
    {
        var expected = new[] { "DUB", "AMS", "LHR", "CDG", "FRA", "JFK", "ATL", "DXB", "DOH", "SIN", "HND", "SYD" };
        var random = new Random(20260802);
        var selected = Enumerable.Range(0, 1000).Select(_ => ExploreRouteService.SelectHeroHub(random)).ToHashSet();

        Assert.Equal(expected, ExploreRouteService.HeroHubs);
        Assert.Equal(expected.Order(), selected.Order());
    }

    [Fact]
    public async Task Hero_UsesFreshCachedHubAndWarmsPreferredColdHubWithoutBlocking()
    {
        var cache = new FixtureExploreCache(Schedule(Now.AddDays(-1)));
        var provider = new FixtureScheduleProvider([]);
        var warmupQueue = new FixtureHeroWarmupQueue();
        var service = CreateService(provider, cache, warmupQueue: warmupQueue);

        var response = await service.GetHeroRoutesAsync("DXB", CancellationToken.None);

        Assert.Equal("DUB", response.Origin.Code);
        Assert.False(response.IsStale);
        Assert.Empty(provider.Pages);
        Assert.Equal(["DXB"], warmupQueue.Origins);
    }

    [Fact]
    public async Task Hero_UsesRetainedStaleHubImmediatelyAndQueuesRefresh()
    {
        var cache = new FixtureExploreCache(Schedule(Now.AddDays(-31)));
        var provider = new FixtureScheduleProvider([]);
        var warmupQueue = new FixtureHeroWarmupQueue();
        var service = CreateService(provider, cache, warmupQueue: warmupQueue);

        var response = await service.GetHeroRoutesAsync("DXB", CancellationToken.None);

        Assert.Equal("DUB", response.Origin.Code);
        Assert.True(response.IsStale);
        Assert.Empty(provider.Pages);
        Assert.Equal(["DXB"], warmupQueue.Origins);
    }

    [Fact]
    public async Task Hero_ColdCacheReturnsPageOnePreviewAndQueuesCompleteWarmup()
    {
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = PageFor("DXB", 1, 6, Destination("AMS", "Amsterdam", "Amsterdam", "Netherlands", 52.31, 4.76))
        });
        var cache = new FixtureExploreCache();
        var warmupQueue = new FixtureHeroWarmupQueue();
        var service = CreateService(provider, cache, warmupQueue: warmupQueue);

        var response = await service.GetHeroRoutesAsync("DXB", CancellationToken.None);

        Assert.Equal("DXB", response.Origin.Code);
        Assert.Equal("AMS", Assert.Single(response.Destinations).Code);
        Assert.False(response.IsComplete);
        Assert.Equal([1], provider.Pages);
        Assert.Equal(0, cache.SetCount);
        Assert.Equal(["DXB"], warmupQueue.Origins);
    }

    [Fact]
    public async Task Hero_ColdCacheMalformedPreviewBecomesSafeProviderFailure()
    {
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = new()
        });
        var service = CreateService(provider, new FixtureExploreCache());

        await Assert.ThrowsAsync<ExploreProviderUnavailableException>(() =>
            service.GetHeroRoutesAsync("DXB", CancellationToken.None));
    }

    [Fact]
    public async Task HeroWarmupQueue_DeduplicatesPendingOriginsUntilCompletion()
    {
        var queue = new HeroRouteWarmupQueue();

        Assert.True(queue.TryEnqueue(" dxb "));
        Assert.False(queue.TryEnqueue("DXB"));
        await using var reader = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("DXB", reader.Current);
        queue.MarkComplete("DXB");
        Assert.True(queue.TryEnqueue("DXB"));
    }

    private static ExploreRouteService CreateService(
        IAirportScheduleProvider provider,
        FixtureExploreCache cache,
        int maxPages = 10,
        ILogger<ExploreRouteService>? logger = null,
        IAirportCatalogRepository? catalog = null,
        IHeroRouteWarmupQueue? warmupQueue = null) =>
        new(
            provider,
            catalog ?? new FixtureAirportCatalog(),
            cache,
            new ProviderRequestCoalescer(),
            warmupQueue ?? new FixtureHeroWarmupQueue(),
            Options.Create(new RedisOptions()),
            Options.Create(new FlightApiOptions { MaxSchedulePages = maxPages }),
            new FixedTimeProvider(Now),
            logger ?? NullLogger<ExploreRouteService>.Instance);

    private static ExploreScheduleCacheEntry Schedule(DateTimeOffset fetchedAt) => new(
        "DUB",
        ["AMS"],
        new(2026, 7, 28),
        new(2026, 8, 7),
        fetchedAt,
        true);

    private static FlightApiScheduleResponse Page(int current, int total, params FlightApiScheduleDestination[] destinations) =>
        PageFor("DUB", current, total, destinations);

    private static FlightApiScheduleResponse PageFor(string origin, int current, int total, params FlightApiScheduleDestination[] destinations) => new()
    {
        Airport = new()
        {
            PluginData = new()
            {
                Details = new()
                {
                    Name = "Dublin Airport",
                    Code = new() { Iata = origin },
                    Position = new() { Latitude = 53.42, Longitude = -6.27, Country = new() { Name = "Ireland" }, Region = new() { City = "Dublin" } }
                },
                Schedule = new()
                {
                    Departures = new()
                    {
                        Page = new() { Current = current, Total = total },
                        Data = destinations.Select(destination => new FlightApiScheduleRow { Flight = new() { Airport = new() { Destination = destination } } }).ToList()
                    }
                }
            }
        }
    };

    private static FlightApiScheduleDestination Destination(string code, string name, string city, string country, double latitude, double longitude) => new()
    {
        Code = new() { Iata = code },
        Name = name,
        Position = new() { Latitude = latitude, Longitude = longitude, Country = new() { Name = country }, Region = new() { City = city } }
    };

    private static FlightApiScheduleV2Response V2Page(params string[] destinations) =>
        V2Page(destinations, "DUB");

    private static FlightApiScheduleV2Response V2Page(string[] destinations, string origin) => new()
    {
        Data = new()
        {
            Header = new() { DepartureAirport = new() { Fs = origin } },
            Flights = destinations.Select(code => new FlightApiScheduleV2Flight { Airport = new() { Fs = code, City = code } }).ToList()
        }
    };

    private sealed class FixtureScheduleProvider(
        Dictionary<int, FlightApiScheduleResponse> responses,
        Exception? exception = null,
        Dictionary<int, FlightApiScheduleV2Response>? datedResponses = null,
        Dictionary<int, Exception>? pageFailures = null) : IAirportScheduleProvider
    {
        public List<int> Pages { get; } = [];
        public List<int> V2Pages { get; } = [];
        public Task<FlightApiScheduleResponse> SearchDepartureScheduleAsync(string originAirport, int page, CancellationToken cancellationToken)
        {
            Pages.Add(page);
            if (exception is not null) throw exception;
            if (pageFailures?.TryGetValue(page, out var pageFailure) is true) throw pageFailure;
            return Task.FromResult(responses[page]);
        }

        public Task<FlightApiScheduleV2Response> SearchDepartureScheduleV2Async(string originAirport, DateOnly departureDate, int page, CancellationToken cancellationToken)
        {
            V2Pages.Add(page);
            if (exception is not null) throw exception;
            if (pageFailures?.TryGetValue(page, out var pageFailure) is true) throw pageFailure;
            return Task.FromResult(datedResponses![page]);
        }
    }

    private sealed class BlockingScheduleProvider(FlightApiScheduleResponse response) : IAirportScheduleProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }

        public async Task<FlightApiScheduleResponse> SearchDepartureScheduleAsync(string originAirport, int page, CancellationToken cancellationToken)
        {
            CallCount++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return response;
        }
    }

    private sealed class FixtureExploreCache : IExploreRouteCache
    {
        private readonly Dictionary<string, ExploreScheduleCacheEntry> _entries = new(StringComparer.Ordinal);
        public FixtureExploreCache(ExploreScheduleCacheEntry? initial = null)
        {
            if (initial is not null) _entries[initial.OriginCode] = initial;
        }
        public int SetCount { get; private set; }
        public Task<ExploreScheduleCacheEntry?> GetAsync(string origin, ExploreCacheProfile profile, CancellationToken cancellationToken, DateOnly? departureDate = null)
        {
            _entries.TryGetValue(origin, out var entry);
            return Task.FromResult(entry);
        }
        public Task SetAsync(string origin, ExploreCacheProfile profile, ExploreScheduleCacheEntry entry, CancellationToken cancellationToken, DateOnly? departureDate = null)
        {
            _entries[origin] = entry;
            SetCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixtureHeroWarmupQueue : IHeroRouteWarmupQueue
    {
        public List<string> Origins { get; } = [];
        public bool TryEnqueue(string origin)
        {
            if (Origins.Contains(origin, StringComparer.Ordinal)) return false;
            Origins.Add(origin);
            return true;
        }
        public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public void MarkComplete(string origin) => Origins.Remove(origin);
    }

    private sealed class FixtureAirportCatalog : IAirportCatalogRepository
    {
        private readonly List<AirportCatalogEntry> _airports =
        [
            Airport("DUB", "Dublin Airport", "Dublin", "IE", 53.42, -6.27),
            Airport("DXB", "Dubai International", "Dubai", "AE", 25.25, 55.36),
            Airport("AMS", "Amsterdam Schiphol", "Amsterdam", "NL", 52.31, 4.76),
            Airport("JFK", "John F. Kennedy International", "New York", "US", 40.64, -73.78)
        ];

        public Task<IReadOnlyDictionary<string, AirportCatalogEntry>> GetByIataCodesAsync(IEnumerable<string> iataCodes, CancellationToken cancellationToken)
        {
            var requested = iataCodes.ToHashSet(StringComparer.Ordinal);
            IReadOnlyDictionary<string, AirportCatalogEntry> result = _airports
                .Where(airport => requested.Contains(airport.Iata))
                .ToDictionary(airport => airport.Iata, StringComparer.Ordinal);
            return Task.FromResult(result);
        }

        public void SetAirportName(string code, string name) => _airports.Single(airport => airport.Iata == code).Name = name;

        public Task<AirportCatalogMetadata?> GetMetadataAsync(CancellationToken cancellationToken) => Task.FromResult<AirportCatalogMetadata?>(null);
        public Task<bool> IsLiveCatalogIntactAsync(int expectedRowCount, IReadOnlyCollection<string> requiredIataCodes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasStagingRowsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeleteAbandonedStagingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceAsync(IReadOnlyCollection<AirportCatalogEntry> airports, AirportCatalogImportSource source, DateTimeOffset importedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordUnchangedAsync(AirportCatalogImportSource source, DateTimeOffset attemptedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordFailureAsync(AirportCatalogImportSource source, DateTimeOffset attemptedAt, string summary, CancellationToken cancellationToken) => throw new NotSupportedException();

        private static AirportCatalogEntry Airport(string iata, string name, string city, string country, double latitude, double longitude) => new()
        {
            Iata = iata,
            Name = name,
            City = city,
            CountryCode = country,
            Latitude = latitude,
            Longitude = longitude,
            SourceUpdatedAt = Now
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
