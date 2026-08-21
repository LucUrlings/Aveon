# Aveon

Aveon is an open-source flight discovery application for searching flexible dates, multiple airports, and complete multi-destination journeys.

Most flight tools ask travellers to commit to one route and one pair of dates before showing any options. Aveon searches the wider set, delivers useful results progressively, and helps the traveller choose an outbound before building compatible return options. It is a metasearch product, not a booking engine. Purchases are completed directly with the fare provider.

Production: [aveon.lucurlings.nl](https://aveon.lucurlings.nl)

The root URL is the product overview. Use `/search` for flexible one-way and return search, `/explore` to map direct destinations for an exact first leave date and discover onward route suggestions, `/build-route` for an exact ordered itinerary, or `/optimize-trip` for bounded journey optimization. Existing `/multi-destination` links are redirected to the matching standalone page, and shared root URLs containing search criteria are redirected to `/search` with their state intact.

Product roadmap: [Multi-Destination Travel Search Product Plan](docs/multi-destination-search-plan.md)

Explore roadmap and implementation contract: [Explore Routes and Homepage Globe Plan](docs/explore-routes-plan.md)

## Implementation Status

The multi-destination foundation, ordered-route search, feasibility engine, bounded optimizer, optimizer frontend, and release hardening are implemented. These correspond to Milestones 0–4 and 6 in the product plan. The direct-route Explore experience, onward discovery flow, random-hub homepage globe, and durable monthly airport catalogue are also implemented; their contract and remaining live-FlightAPI operational smoke-test item are tracked separately in the Explore plan.

The former Milestone 5 investigated FlightAPI Multi Trip bundled fares. The configured FlightAPI subscription does not provide that API, so the experimental implementation was removed and the work is explicitly deferred. Current multi-destination results are assembled from independently bookable one-way fares.

Multi-destination search is enabled by default and remains independently switchable through `MultiDestinationSearch:Enabled`. Set it to `false` for an immediate rollback without affecting simple one-way and return search.

## Features

- One-way and return searches
- Multiple origin and destination airports
- Date ranges and individually selected travel dates
- Cached airport autocomplete
- Interactive direct-route exploration with a random-destination option
- Random-hub route globe on the product homepage
- Progressive search sessions with live completion status
- Outbound-first return selection to avoid materializing every possible round trip
- Provider round trips and compatible synthetic returns built from two one-way fares
- Recommended, cheapest, and fastest return rankings
- Filters for stops, providers, airlines, airports, duration, and departure or arrival times
- Automatic fallback to the exact fewest available stops when no direct or one-stop result exists
- Filter-aware stop counts and explicit outbound-only price labels during return selection
- Backend pagination with automatic infinite loading and a manual fallback
- Grouped provider fares and locale-aware booking links
- Shareable URL-backed search and filter state
- Cookie-based registration and sign-in
- Different search limits for guests, registered users, and administrators
- Responsive and accessible search controls
- Ordered multi-destination routes with multiple airports at every endpoint
- Bounded trip optimization with optional destination reordering, stay rules, endpoint modes, and airport continuity
- Complete-itinerary ranking, filtering, progressive coverage, and separate-booking warnings
- Route-specific titles, descriptions, canonical URLs, structured data, `robots.txt`, and `sitemap.xml`

Aveon currently integrates with FlightAPI. It does not process payments, issue tickets, or guarantee that a provider fare remains available after the user leaves Aveon.

## Technology

- Frontend: Vue 3, Vue Router, Vite, TypeScript, Vitest, Globe.gl, TopoJSON, and World Atlas
- Backend: ASP.NET Core 10, Entity Framework Core, ASP.NET Core Identity, and CsvHelper
- Database: PostgreSQL
- Cache and search sessions: Redis
- Flight data: FlightAPI
- Packaging: Docker and Docker Compose

## Repository Layout

```text
apps/
  backend/
    Features/
      Airports/
      Auth/
      Explore/
      ItinerarySearch/
      Search/
    Infrastructure/
      Airports/
      Auth/
      Caching/
      Persistence/
      Providers/FlightApi/
  backend.Tests/
  frontend/
    config/
    public/
    src/
      components/
        flight-search/
      features/
        auth/
        explore/
        flight-search/
        itinerary-search/
      pages/
    tests/unit/
```

## How Search Works

### Simple one-way and return search

1. The frontend posts the selected airports, departure dates, return dates, passenger count, and cabin class.
2. The backend validates and deduplicates the request, applies the current user's search limit, and creates a Redis-backed search session.
3. Search combinations are processed in the background through one process-wide FlightAPI request gate. Cache lookup happens first, so cached responses do not consume one of the five live-request permits.
4. The frontend polls the session and displays normalized, grouped results as provider calls complete.
5. Results are filtered and paginated by the backend. The frontend requests the next page as the user scrolls.
6. Stop counts are calculated after the other active filters. If the default direct-only view is empty, the UI selects one stop when available, otherwise the exact lowest stop count that still has results.
7. For return searches, the user first selects an outbound leg. Outbound cards explicitly state that their price excludes the return; the backend then returns only compatible inbound options and complete-trip prices for that selection.
8. Real provider round trips remain distinct from synthetic combinations made from separately bookable one-way fares.

This staged return flow prevents the outbound and inbound result sets from producing an unbounded cross-product in memory. The backend also limits provider calls, caps retained fares per direction, and rejects searches beyond the configured safety limits.

### Multi-destination search

- **Build my route** searches an exact sequence of dated legs, with multiple acceptable airports at every endpoint.
- **Optimize my trip** generates valid destination orders and schedules, prices their flight edges, and ranks complete itineraries.
- Coverage is reported as `exhaustive` when the viable search space was covered or `bounded` when a configured provider-call, state, result, or time limit was reached.
- A bounded result is the best complete set Aveon found within its allowance, not a guarantee of the globally cheapest route.
- Current itineraries use separate one-way bookings and display booking-count, airport-change, and disruption-risk warnings before booking actions.

See the in-product [How search works](https://aveon.lucurlings.nl/how-it-works) page for a user-facing explanation.

### Explore routes

- The Explore page asks for an exact first leave date, bounded from today through 365 days ahead, and filters direct destinations with FlightAPI Schedule v2. Schedule v1 supplies rolling route codes for the homepage and undated onward suggestions.
- Airport names, cities, countries, and globe coordinates come from an `airportsdata` catalogue in PostgreSQL. The backend validates and atomically refreshes it about once a month; a failed refresh leaves the previous catalogue active.
- The homepage prefers an already cached random-hub network and warms other selected hubs sequentially in the background. On a completely cold deployment it displays page one as a quick preview instead of blocking on the complete multi-page schedule.
- Redis stores only observed origin/destination codes and schedule metadata. Each response batch-enriches those codes from PostgreSQL, so corrected catalogue data appears without clearing schedule caches.
- Route networks are cached for seven days; homepage hub previews are cached for thirty days, with retained stale data used when FlightAPI is temporarily unavailable.
- Country outlines come from a locally bundled low-resolution world topology, so the globe remains a recognizable map without third-party tile or texture requests.
- Schedule calls share the same process-wide FlightAPI gate as airport autocomplete and every fare-search mode.
- Selecting a destination previews and highlights that direct leg without navigating. The traveler can explicitly open Search with the first date prefilled, or commit the stop and keep exploring; multi-leg paths hand off to ordered Build my route with only that first date filled and without starting a provider request. A warning explains that onward dates may not operate or return fares.
- Hovering or focusing a destination immediately restarts its highlighted arc, keeps a faint complete route underneath, pauses rotation, and pans toward the airport. Loading, result replacement, selection cards, route breadcrumbs, and filtered destinations transition without blank-map flicker; reduced-motion preferences disable nonessential animation.
- Explore never fetches prices. The exact-date first leg and rolling onward suggestions describe scheduled service, not guaranteed fare availability.

## Important Code Areas

### Backend

- Search orchestration: [`SearchService.cs`](apps/backend/Features/Search/SearchService.cs)
- Multi-destination orchestration: [`ItinerarySearchService.cs`](apps/backend/Features/ItinerarySearch/ItinerarySearchService.cs)
- Multi-destination metrics: [`ItinerarySearchTelemetry.cs`](apps/backend/Features/ItinerarySearch/ItinerarySearchTelemetry.cs)
- Bounded priced optimizer: [`OptimizedItinerarySearchRunner.cs`](apps/backend/Features/ItinerarySearch/OptimizedItinerarySearchRunner.cs)
- Search API: [`SearchController.cs`](apps/backend/Features/Search/SearchController.cs)
- Explore API and route aggregation: [`ExploreRouteService.cs`](apps/backend/Features/Explore/ExploreRouteService.cs)
- Airport catalogue import and validation: [`Infrastructure/Airports`](apps/backend/Infrastructure/Airports)
- Global provider concurrency: [`FlightApiRequestGate.cs`](apps/backend/Infrastructure/Providers/FlightApi/FlightApiRequestGate.cs)
- Identical live-request coalescing: [`ProviderRequestCoalescer.cs`](apps/backend/Infrastructure/Providers/FlightApi/ProviderRequestCoalescer.cs)
- Guest and account limits: [`SearchLimitResolver.cs`](apps/backend/Features/Search/SearchLimitResolver.cs)
- Redis session storage: [`RedisSearchSessionStore.cs`](apps/backend/Infrastructure/Caching/RedisSearchSessionStore.cs)
- Provider response caching: [`RedisProviderResponseCache.cs`](apps/backend/Infrastructure/Caching/RedisProviderResponseCache.cs)
- FlightAPI integration: [`FlightApiClient.cs`](apps/backend/Infrastructure/Providers/FlightApi/FlightApiClient.cs)
- Authentication API: [`AuthController.cs`](apps/backend/Features/Auth/AuthController.cs)
- Database context and migrations: [`Persistence`](apps/backend/Infrastructure/Persistence)

### Frontend

- Product landing page: [`HomePage.vue`](apps/frontend/src/pages/HomePage.vue)
- Route explorer: [`ExplorePage.vue`](apps/frontend/src/pages/ExplorePage.vue)
- Shared route globe: [`RouteGlobe.vue`](apps/frontend/src/features/explore/RouteGlobe.vue)
- Search-page composition: [`FlightSearch.vue`](apps/frontend/src/components/FlightSearch.vue)
- Search execution, polling, cancellation, and pagination: [`useSearchSession.ts`](apps/frontend/src/features/flight-search/useSearchSession.ts)
- URL hydration and synchronization: [`useSearchRouteState.ts`](apps/frontend/src/features/flight-search/useSearchRouteState.ts)
- Airport selection: [`useAirportPicker.ts`](apps/frontend/src/features/flight-search/useAirportPicker.ts)
- Date selection: [`useSearchDates.ts`](apps/frontend/src/features/flight-search/useSearchDates.ts)
- Filters: [`useSearchFilters.ts`](apps/frontend/src/features/flight-search/useSearchFilters.ts)
- Result presentation: [`SearchResultsPanel.vue`](apps/frontend/src/components/flight-search/SearchResultsPanel.vue)
- Return ranking: [`returnRanking.ts`](apps/frontend/src/features/flight-search/returnRanking.ts)
- API normalization: [`api.ts`](apps/frontend/src/features/flight-search/api.ts)
- Authentication state: [`useAuth.ts`](apps/frontend/src/features/auth/useAuth.ts)
- SEO metadata: [`seo.ts`](apps/frontend/src/seo.ts)
- Route definitions: [`router.ts`](apps/frontend/src/router.ts)
- Generated API types: [`generated.ts`](apps/frontend/src/api/generated.ts)
- Privacy-bounded multi-destination analytics: [`analytics.ts`](apps/frontend/src/features/itinerary-search/analytics.ts)

## FlightAPI deployment constraint

All FlightAPI operations—including airport autocomplete, departure-schedule pages, one-way, round-trip, and multi-destination edge searches—must issue live HTTP requests through the singleton `FlightApiRequestGate`. The configured allowance defaults to five concurrent requests across the whole backend process and can be raised when the FlightAPI subscription permits it. Cache hits bypass the gate, identical concurrent cache misses share one in-flight request, and retries reacquire one permit per live attempt. Each waiter may cancel independently: shared work continues while another waiter still needs it and is canceled once no waiters remain.

Aveon currently supports one backend application instance. Horizontal scaling is not safe until the process-local gate is replaced by a Redis-backed distributed lease or the provider allowance is divided explicitly between instances.

## Local Development

### Prerequisites

- .NET 10 SDK
- Node.js 22 or newer
- `pnpm`
- Docker
- A FlightAPI API key

### Install dependencies

From the repository root:

```bash
pnpm install
```

### Configure FlightAPI

Use .NET user secrets for local backend development:

```bash
cd apps/backend
dotnet user-secrets set "FlightApi:ApiKey" "your-key"
cd ../..
```

Do not commit the API key to `appsettings.json`, `.env`, or source control.

Multi-destination search is enabled by default. To exercise the rollback state locally, disable it with a user secret:

```bash
cd apps/backend
dotnet user-secrets set "MultiDestinationSearch:Enabled" "false"
cd ../..
```

### Start the application

```bash
pnpm dev
```

This starts PostgreSQL and Redis through Docker Compose, then runs:

- frontend: `http://localhost:5173`
- backend and Swagger: `http://localhost:5210`

The Vite development server proxies `/api` to `http://localhost:5210`, so frontend requests remain same-origin even when Vite selects a port other than `5173`. Override the target with `VITE_DEV_API_TARGET` when the backend runs elsewhere.

The services can also be started separately:

```bash
pnpm dev:infra
pnpm dev:frontend
pnpm dev:backend
```

Local infrastructure defaults:

- PostgreSQL: `localhost:5433`
- Redis: `localhost:6379`
- database: `aveon`
- username: `aveon`
- password: `aveon_dev_password`

## Configuration

Copy the example environment file before using the production Compose stack:

```bash
cp .env.example .env
```

Important settings:

| Variable | Purpose | Default |
| --- | --- | --- |
| `FLIGHTAPI_API_KEY` | FlightAPI credential | Required |
| `FLIGHTAPI_MAX_CONCURRENT_REQUESTS` | Process-wide live FlightAPI request limit | `5` |
| `FLIGHTAPI_MAX_SCHEDULE_PAGES` | Defensive maximum pages per Airport Schedule aggregation | `10` |
| `AVEON_PUBLIC_URL` | Public origin used for canonical metadata, `robots.txt`, and `sitemap.xml` | Required in the container |
| `AVEON_PORT` | Host port for the application container | `8080` |
| `POSTGRES_DB` | PostgreSQL database | `aveon` |
| `POSTGRES_USER` | PostgreSQL username | `aveon` |
| `POSTGRES_PASSWORD` | PostgreSQL password | Development value only |
| `POSTGRES_DEV_PORT` | Local PostgreSQL host port | `5433` |
| `REDIS_DEV_PORT` | Local Redis host port | `6379` |
| `REDIS_FLIGHT_API_ONE_WAY_TTL_MINUTES` | Provider-response cache lifetime | `30` |
| `REDIS_AIRPORT_DATA_TTL_MINUTES` | Airport lookup cache lifetime | `10080` |
| `REDIS_EXPLORE_ROUTES_TTL_MINUTES` | Explore route-network freshness | `10080` |
| `REDIS_HERO_ROUTES_TTL_MINUTES` | Homepage route-network freshness | `43200` |
| `REDIS_EXPLORE_ROUTES_RETENTION_MINUTES` | Explore stale-cache retention | `43200` |
| `REDIS_HERO_ROUTES_RETENTION_MINUTES` | Homepage stale-cache retention | `129600` |
| `REDIS_SEARCH_SESSION_TTL_MINUTES` | Search-session lifetime | `30` |
| `AIRPORT_CATALOG_REFRESH_ENABLED` | Enable the startup and periodic airport-catalogue refresh worker | `true` |
| `AIRPORT_CATALOG_SOURCE_URL` | Stable upstream catalogue URL used for provenance and pre-revision failure metadata | `airportsdata` raw CSV |
| `AIRPORT_CATALOG_REVISION_API_URL` | GitHub commits query for the latest commit affecting `airports.csv` | `airportsdata` commits API |
| `AIRPORT_CATALOG_REVISION_DOWNLOAD_URL_TEMPLATE` | HTTPS raw-file template pinned with the discovered `{revision}` SHA | `airportsdata` commit-pinned CSV |
| `AIRPORT_CATALOG_REFRESH_AGE_DAYS` | Age after which the catalogue is checked again | `30` |
| `SEARCH_ANONYMOUS_MAX_SEARCH_COMBINATIONS` | Guest search limit | `75` |
| `SEARCH_USER_MAX_SEARCH_COMBINATIONS` | Registered-user search limit | `200` |
| `SEARCH_EXECUTION_TIMEOUT_MINUTES` | Simple-search worker timeout | `10` |
| `MULTI_DESTINATION_SEARCH_ENABLED` | Independent multi-destination feature flag | `true` |
| `MULTI_DESTINATION_ANONYMOUS_MAX_PROVIDER_CALLS` | Guest multi-destination live-call budget | `25` |
| `MULTI_DESTINATION_USER_MAX_PROVIDER_CALLS` | Registered-user multi-destination live-call budget | `100` |
| `MULTI_DESTINATION_ADMIN_MAX_PROVIDER_CALLS` | Administrator multi-destination live-call budget | `250` |
| `MULTI_DESTINATION_EXECUTION_TIMEOUT_MINUTES` | Ordered and optimized worker timeout | `10` |
| `MULTI_DESTINATION_MAX_ACTIVE_STATES` | Maximum candidate paths retained in an optimizer frontier | `10000` |
| `MULTI_DESTINATION_MAX_EVALUATED_STATES` | Maximum cumulative optimizer path evaluations | `500000` |

`AVEON_PUBLIC_URL` must be an HTTP or HTTPS origin without a path. For the public deployment, use:

```dotenv
AVEON_PUBLIC_URL=https://aveon.lucurlings.nl
```

Development cache lifetimes in `appsettings.Development.json` are intentionally longer than the production defaults.

### Airport catalogue operations

The backend checks the PostgreSQL catalogue at startup and every 24 hours. Once the last successful confirmation is at least 30 days old—or the stored revision is missing/legacy, or the live row count/required hubs no longer match that confirmation—it asks GitHub for the latest commit that changed `airportsdata/airports.csv`. That commit SHA is the stored source revision. If it matches the stored SHA and the live catalogue is intact, the backend records the confirmation without downloading the CSV. A changed SHA, missing catalogue, or damaged live catalogue triggers a download from the raw URL pinned to that exact SHA; a changed SHA whose bytes have the same checksum updates only the metadata.

Imports are protected by a PostgreSQL advisory lock, which also makes it safe to delete staging batches abandoned by a terminated importer. A downloaded file is parsed fully before live data changes, checked for malformed quoting, database text-length boundaries, duplicate IATA codes, invalid coordinates, missing homepage hubs, implausible size, and an exact greater-than-ten-percent row drop (including non-round catalogue sizes), then promoted from staging in one transaction. Revision lookup, download, parsing, validation, or database failures are recorded and retain the previous live catalogue. There is deliberately no fallback to an unpinned branch download when GitHub cannot supply a valid revision; caller cancellation also retains the catalogue but is not misreported as a source failure.

Administrators can force the guarded recovery path with `POST /api/v1/explore/catalog/refresh`. A forced refresh always downloads the commit-pinned CSV and performs validation, staging, and transactional replacement, even when its SHA and checksum are unchanged; it never bypasses import safety checks. The endpoint requires an authenticated account with the `Admin` role. Import status is stored in `airport_catalog_metadata`; `airports` is the live catalogue and `airport_catalog_staging` should be empty outside an active refresh. A missing initial catalogue makes Explore return a temporary `503` until a valid import completes. Do not manually truncate the live table to recover from a failed refresh.

The source dataset and required notice are documented in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

See [multi-destination rollout and rollback](docs/multi-destination-rollout.md) before changing the feature state. The feature is enabled by default and can be disabled without affecting simple search.

## Observability and Analytics

Backend instruments use the `Aveon.FlightApi` and `Aveon.ItinerarySearch` meters. They report provider concurrency, queued work, cache hits, throttling, active searches, completion status, coverage, duration, result counts, and live provider-call counts. Structured airport-catalogue logs report due/locked/unchanged states, import duration, checksums, imported and rejected row counts, failures, and missing enrichment codes without logging dataset bodies.

Lifecycle logs use structured fields and deliberately omit API keys, booking URLs, and booking tokens. Frontend analytics cover form abandonment, validation failure, completed searches, bounded coverage, result selection, and booking clicks. Analytics properties are allow-listed and do not include search IDs, selected airports, or booking URLs.

## Testing and Validation

Run the backend tests:

```bash
dotnet test apps/backend.Tests/backend.Tests.csproj
```

Run the frontend tests:

```bash
pnpm --dir apps/frontend test
```

Run a production frontend build:

```bash
AVEON_PUBLIC_URL=https://aveon.lucurlings.nl pnpm --dir apps/frontend build
```

Frontend coverage includes simple and multi-destination search sessions, Explore and homepage globes, onward-route history, WebGL fallback, reduced motion, analytics privacy, stale-request cancellation, pagination, route synchronization, API normalization, authentication races, ranking, filtering, accessibility, date handling, and SEO generation. Backend coverage includes request validation, Explore schedule aggregation and cache fallback, maximum-input bounds, ordered and optimized orchestration, telemetry, staged returns, pagination, provider-call limiting, user limits, controllers, cache keys, airport lookup, and FlightAPI behavior.

## API Type Generation

Frontend API types are generated from the backend Swagger document. With the backend running on `http://localhost:5210`:

```bash
pnpm --dir apps/frontend generate:types
```

Review the generated diff and run the frontend tests after regeneration.

## Production Deployment

The multi-stage [`Dockerfile`](Dockerfile) builds the Vue frontend, copies it into the backend's `wwwroot`, publishes the ASP.NET application, and produces one runtime image.

[`docker-compose.yml`](docker-compose.yml) runs:

- the Aveon application
- PostgreSQL
- Redis with persistent storage

Start it with:

```bash
docker compose up -d
```

Multi-destination search is enabled by default. Follow the [rollout and rollback checklist](docs/multi-destination-rollout.md) when changing its deployment state. Rollback requires setting `MULTI_DESTINATION_SEARCH_ENABLED=false`; it does not require a database migration or Redis deletion.

The container entrypoint injects `AVEON_PUBLIC_URL` into the prebuilt HTML and writes `robots.txt` and `sitemap.xml` into `wwwroot` before ASP.NET starts. This keeps deployment-specific SEO URLs out of the backend application code.

The GitHub Actions workflow in [`.github/workflows/publish-image.yml`](.github/workflows/publish-image.yml) publishes:

```text
ghcr.io/lucurlings/aveon:latest
```

## Current Constraints

- FlightAPI is the only flight-data provider.
- Provider rate limits and upstream failures still affect result completeness.
- Prices and availability can change between discovery and booking.
- Synthetic returns are separate bookings and can have different provider terms.
- Multi-destination itineraries currently use separately bookable one-way fares; FlightAPI bundled Multi Trip fares are deferred.
- Explore uses Schedule v2 for its exact-date first leg, Schedule v1 for homepage and rolling onward route codes, and the monthly PostgreSQL catalogue for airport metadata. It never proves fare availability.
- Bounded optimized coverage does not guarantee the globally cheapest possible route.
- Search history and saved itineraries are not currently persisted for users.
- The frontend is a client-rendered Vue application. Static metadata and structured files provide the initial SEO surface, while route metadata is updated in the browser.

## License

See [`LICENSE`](LICENSE). Third-party data notices are listed in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
