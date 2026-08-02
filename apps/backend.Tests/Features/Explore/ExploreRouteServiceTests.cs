using backend.Features.Explore;
using backend.Features.Explore.Models;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
            [1] = Page(1, 2, Destination("AMS", "Amsterdam Schiphol", "Amsterdam", "Netherlands", 52.31, 4.76), Destination("AMS", "Amsterdam Schiphol", "Amsterdam", "Netherlands", 52.31, 4.76), Destination("BAD", "Missing", "Nowhere", "", 0, 0), Destination("LAT", "Invalid latitude", "Nowhere", "", 91, 10), Destination("LON", "Invalid longitude", "Nowhere", "", 10, 181)),
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
        var cached = Routes(Now.AddDays(-1));
        var cache = new FixtureExploreCache(new(cached, cached.FetchedAt));
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
            [1] = Page(1, 1)
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
        var cached = Routes(Now.AddDays(-8));
        var cache = new FixtureExploreCache(new(cached, cached.FetchedAt));
        var provider = new FixtureScheduleProvider([], new HttpRequestException("provider unavailable"));
        var service = CreateService(provider, cache);

        var response = await service.GetRoutesAsync("DUB", ExploreCacheProfile.Explore, CancellationToken.None);

        Assert.True(response.IsStale);
        Assert.Single(provider.Pages);
        Assert.Equal(0, cache.SetCount);
    }

    [Fact]
    public async Task MonthOldHeroProfile_RemainsFreshLongerThanExploreProfile()
    {
        var cached = Routes(Now.AddDays(-8));
        var cache = new FixtureExploreCache(new(cached, cached.FetchedAt));
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
    public async Task LaterPageFailure_ReturnsPartialNetworkAndDoesNotCacheIt()
    {
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = Page(1, 2, Destination("AMS", "Amsterdam", "Amsterdam", "Netherlands", 52.31, 4.76))
        });
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
        var cached = Routes(Now.AddDays(-8));
        var cache = new FixtureExploreCache(new(cached, cached.FetchedAt));
        var provider = new FixtureScheduleProvider(new Dictionary<int, FlightApiScheduleResponse>
        {
            [1] = Page(1, 2, Destination("JFK", "New York JFK", "New York", "United States", 40.64, -73.78))
        });
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
    public void HeroSelection_UsesEveryCuratedHubAndNoOthers()
    {
        var expected = new[] { "DUB", "AMS", "LHR", "CDG", "FRA", "JFK", "ATL", "DXB", "DOH", "SIN", "HND", "SYD" };
        var random = new Random(20260802);
        var selected = Enumerable.Range(0, 1000).Select(_ => ExploreRouteService.SelectHeroHub(random)).ToHashSet();

        Assert.Equal(expected, ExploreRouteService.HeroHubs);
        Assert.Equal(expected.Order(), selected.Order());
    }

    private static ExploreRouteService CreateService(
        IAirportScheduleProvider provider,
        FixtureExploreCache cache,
        int maxPages = 10,
        ILogger<ExploreRouteService>? logger = null) =>
        new(
            provider,
            cache,
            new ProviderRequestCoalescer(),
            Options.Create(new RedisOptions()),
            Options.Create(new FlightApiOptions { MaxSchedulePages = maxPages }),
            new FixedTimeProvider(Now),
            logger ?? NullLogger<ExploreRouteService>.Instance);

    private static ExploreRoutesResponse Routes(DateTimeOffset fetchedAt) => new(
        new("DUB", "Dublin Airport", "Dublin", "Ireland", 53.42, -6.27),
        [new("AMS", "Amsterdam Schiphol", "Amsterdam", "Netherlands", 52.31, 4.76)],
        new(2026, 7, 28),
        new(2026, 8, 7),
        fetchedAt,
        true,
        false);

    private static FlightApiScheduleResponse Page(int current, int total, params FlightApiScheduleDestination[] destinations) => new()
    {
        Airport = new()
        {
            PluginData = new()
            {
                Details = new()
                {
                    Name = "Dublin Airport",
                    Code = new() { Iata = "DUB" },
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

    private sealed class FixtureScheduleProvider(Dictionary<int, FlightApiScheduleResponse> responses, Exception? exception = null) : IAirportScheduleProvider
    {
        public List<int> Pages { get; } = [];
        public Task<FlightApiScheduleResponse> SearchDepartureScheduleAsync(string originAirport, int page, CancellationToken cancellationToken)
        {
            Pages.Add(page);
            if (exception is not null) throw exception;
            return Task.FromResult(responses[page]);
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

    private sealed class FixtureExploreCache(ExploreRouteCacheEntry? initial = null) : IExploreRouteCache
    {
        private ExploreRouteCacheEntry? _entry = initial;
        public int SetCount { get; private set; }
        public Task<ExploreRouteCacheEntry?> GetAsync(string origin, ExploreCacheProfile profile, CancellationToken cancellationToken)
        {
            return Task.FromResult(_entry);
        }
        public Task SetAsync(string origin, ExploreCacheProfile profile, ExploreRouteCacheEntry entry, CancellationToken cancellationToken)
        {
            _entry = entry;
            SetCount++;
            return Task.CompletedTask;
        }
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
