using backend.Infrastructure.Auth;
using backend.Infrastructure.Airports;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace backend.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<AirportCatalogEntry> Airports => Set<AirportCatalogEntry>();

    public DbSet<AirportCatalogStagingEntry> AirportCatalogStaging => Set<AirportCatalogStagingEntry>();

    public DbSet<AirportCatalogMetadata> AirportCatalogMetadata => Set<AirportCatalogMetadata>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AirportCatalogEntry>(entity =>
        {
            entity.ToTable("airports");
            entity.HasKey(airport => airport.Iata);
            entity.Property(airport => airport.Iata).HasMaxLength(3);
            entity.Property(airport => airport.Icao).HasMaxLength(4);
            entity.Property(airport => airport.Name).HasMaxLength(256);
            entity.Property(airport => airport.City).HasMaxLength(128);
            entity.Property(airport => airport.Subdivision).HasMaxLength(128);
            entity.Property(airport => airport.CountryCode).HasMaxLength(2);
            entity.Property(airport => airport.Timezone).HasMaxLength(128);
            entity.HasIndex(airport => airport.CountryCode);
        });

        builder.Entity<AirportCatalogStagingEntry>(entity =>
        {
            entity.ToTable("airport_catalog_staging");
            entity.HasKey(airport => new { airport.BatchId, airport.Iata });
            entity.Property(airport => airport.Iata).HasMaxLength(3);
            entity.Property(airport => airport.Icao).HasMaxLength(4);
            entity.Property(airport => airport.Name).HasMaxLength(256);
            entity.Property(airport => airport.City).HasMaxLength(128);
            entity.Property(airport => airport.Subdivision).HasMaxLength(128);
            entity.Property(airport => airport.CountryCode).HasMaxLength(2);
            entity.Property(airport => airport.Timezone).HasMaxLength(128);
        });

        builder.Entity<AirportCatalogMetadata>(entity =>
        {
            entity.ToTable("airport_catalog_metadata");
            entity.HasKey(metadata => metadata.Id);
            entity.Property(metadata => metadata.SourceName).HasMaxLength(128);
            entity.Property(metadata => metadata.SourceUrl).HasMaxLength(1024);
            entity.Property(metadata => metadata.SourceRevision).HasMaxLength(256);
            entity.Property(metadata => metadata.SourceChecksum).HasMaxLength(64);
            entity.Property(metadata => metadata.LastFailureSummary).HasMaxLength(1024);
        });
    }
}
