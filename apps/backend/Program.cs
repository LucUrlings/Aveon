using backend.Features.Airports;
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
builder.Services.AddSwaggerGen();
builder.Services.Configure<FlightApiOptions>(
    builder.Configuration.GetSection(FlightApiOptions.SectionName));
builder.Services.Configure<RedisOptions>(
    builder.Configuration.GetSection(RedisOptions.SectionName));
builder.Services.Configure<SearchOptions>(
    builder.Configuration.GetSection(SearchOptions.SectionName));
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
builder.Services.AddScoped<ISearchLimitResolver, SearchLimitResolver>();
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
builder.Services.AddHttpClient<FlightApiClient>((serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<FlightApiOptions>>()
        .Value;

    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.AddScoped<IFlightSearchProvider>(serviceProvider => serviceProvider.GetRequiredService<FlightApiClient>());
builder.Services.AddScoped<IAirportLookupProvider>(serviceProvider => serviceProvider.GetRequiredService<FlightApiClient>());

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
