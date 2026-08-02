using backend.Features.Airports;
using backend.Features.Explore;
using backend.Features.ItinerarySearch;
using backend.Features.ItinerarySearch.Models;
using backend.Features.Search;
using backend.Features.Search.Models;
using backend.Infrastructure.Auth;
using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Persistence;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
// builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.UseOneOfForPolymorphism();
    options.UseAllOfToExtendReferenceSchemas();
    options.SelectSubTypesUsing(baseType => baseType == typeof(ItinerarySearchRequest)
        ? [typeof(OptimizedTripRequest), typeof(OrderedTripRequest)]
        : []);
    options.SelectDiscriminatorNameUsing(baseType => baseType == typeof(ItinerarySearchRequest) ? "mode" : null);
    options.SelectDiscriminatorValueUsing(subType => subType == typeof(OptimizedTripRequest)
        ? "optimize"
        : subType == typeof(OrderedTripRequest) ? "ordered" : null);
});
builder.Services.AddOptions<FlightApiOptions>()
    .Bind(builder.Configuration.GetSection(FlightApiOptions.SectionName))
    .Validate(options => options.MaxConcurrentRequests > 0, "FlightApi:MaxConcurrentRequests must be positive.")
    .Validate(options => options.MaxRetryAttempts > 0 && options.MaxRetryDelaySeconds > 0 && options.MaxSchedulePages > 0, "FlightAPI retry and schedule-page limits must be positive.")
    .ValidateOnStart();
builder.Services.Configure<RedisOptions>(
    builder.Configuration.GetSection(RedisOptions.SectionName));
builder.Services.AddOptions<SearchOptions>()
    .Bind(builder.Configuration.GetSection(SearchOptions.SectionName))
    .Validate(options => options.ExecutionTimeoutMinutes > 0, "Search:ExecutionTimeoutMinutes must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<MultiDestinationSearchOptions>()
    .Bind(builder.Configuration.GetSection(MultiDestinationSearchOptions.SectionName))
    .Validate(MultiDestinationSearchOptionsValidation.HasPositiveLimits, "Multi-destination search limits and provider budgets must be positive.")
    .Validate(MultiDestinationSearchOptionsValidation.HasOrderedProviderBudgets, "Multi-destination provider-call limits must increase by role and remain under the hard limit.")
    .ValidateOnStart();
var databaseConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(databaseConnectionString));
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Events = new CookieAuthenticationEvents
    {
        OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        },
        OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        },
    };
});
builder.Services.AddScoped<IAirportService, AirportService>();
builder.Services.AddScoped<IExploreRouteService, ExploreRouteService>();
builder.Services.AddScoped<ISearchLimitResolver, SearchLimitResolver>();
builder.Services.AddSingleton<IFlightApiRequestGate, FlightApiRequestGate>();
builder.Services.AddSingleton<IProviderRequestCoalescer, ProviderRequestCoalescer>();
builder.Services.AddSingleton<IItinerarySearchSessionStore, RedisItinerarySearchSessionStore>();
builder.Services.AddSingleton<IOrderedItinerarySearchRunner, OrderedItinerarySearchRunner>();
builder.Services.AddSingleton<IOptimizedScheduleGenerator, OptimizedScheduleGenerator>();
builder.Services.AddSingleton<IOptimizedItinerarySearchRunner, OptimizedItinerarySearchRunner>();
builder.Services.AddSingleton<IItinerarySearchService, ItinerarySearchService>();
builder.Services.AddSingleton<ISearchSessionStore, RedisSearchSessionStore>();
builder.Services.AddSingleton<ISearchService, SearchService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisOptions>>()
        .Value;

    return ConnectionMultiplexer.Connect(options.ConnectionString);
});
builder.Services.AddSingleton<IProviderResponseCache, RedisProviderResponseCache>();
builder.Services.AddSingleton<IExploreRouteCache, RedisExploreRouteCache>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<FlightApiClient>((serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<FlightApiOptions>>()
        .Value;

    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.AddScoped<IFlightSearchProvider>(serviceProvider => serviceProvider.GetRequiredService<FlightApiClient>());
builder.Services.AddScoped<IAirportLookupProvider>(serviceProvider => serviceProvider.GetRequiredService<FlightApiClient>());
builder.Services.AddScoped<IAirportScheduleProvider>(serviceProvider => serviceProvider.GetRequiredService<FlightApiClient>());

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("frontend");

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

var hasWebRoot = Directory.Exists(app.Environment.WebRootPath);
if (hasWebRoot)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
if (hasWebRoot)
{
    app.MapFallbackToFile("index.html");
}

await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();
