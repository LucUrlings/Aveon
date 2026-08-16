using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace backend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAirportCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "airport_catalog_metadata",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SourceRevision = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSuccessfulImportAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ImportedRowCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptedRefreshAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastFailureSummary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_airport_catalog_metadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "airport_catalog_staging",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Iata = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Icao = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Subdivision = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    ElevationFeet = table.Column<int>(type: "integer", nullable: true),
                    Timezone = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_airport_catalog_staging", x => new { x.BatchId, x.Iata });
                });

            migrationBuilder.CreateTable(
                name: "airports",
                columns: table => new
                {
                    Iata = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Icao = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Subdivision = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    ElevationFeet = table.Column<int>(type: "integer", nullable: true),
                    Timezone = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_airports", x => x.Iata);
                });

            migrationBuilder.CreateIndex(
                name: "IX_airports_CountryCode",
                table: "airports",
                column: "CountryCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "airport_catalog_metadata");

            migrationBuilder.DropTable(
                name: "airport_catalog_staging");

            migrationBuilder.DropTable(
                name: "airports");
        }
    }
}
