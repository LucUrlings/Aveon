# Aveon

Aveon is an open-source flight discovery application for searching flexible dates, multiple airports, and complete multi-destination journeys.

Most flight tools ask travellers to commit to one route and one pair of dates before showing any options. Aveon searches the wider set, delivers useful results progressively, and helps the traveller choose an outbound before building compatible return options. It is a metasearch product, not a booking engine. Purchases are completed directly with the fare provider.

Production: [aveon.lucurlings.nl](https://aveon.lucurlings.nl)

The root URL is the product overview. Use `/search` for flexible one-way and return search, or `/multi-destination` for ordered and optimized journeys. Existing shared root URLs containing search criteria are redirected to `/search` with their state intact.

Product roadmap: [Multi-Destination Travel Search Product Plan](docs/multi-destination-search-plan.md)

## Implementation Status

The multi-destination foundation, ordered-route search, feasibility engine, bounded optimizer, optimizer frontend, and release hardening are implemented. These correspond to Milestones 0–4 and 6 in the product plan.

The former Milestone 5 investigated FlightAPI Multi Trip bundled fares. The configured FlightAPI subscription does not provide that API, so the experimental implementation was removed and the work is explicitly deferred. Current multi-destination results are assembled from independently bookable one-way fares.

Multi-destination search is enabled by default and remains independently switchable through `MultiDestinationSearch:Enabled`. Set it to `false` for an immediate rollback without affecting simple one-way and return search.

## Features

- One-way and return searches
- Multiple origin and destination airports
- Date ranges and individually selected travel dates
- Cached airport autocomplete
- Progressive search sessions with live completion status
- Outbound-first return selection to avoid materializing every possible round trip
- Provider round trips and compatible synthetic returns built from two one-way fares
- Recommended, cheapest, and fastest return rankings
- Filters for stops, providers, airlines, airports, duration, and departure or arrival times
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

- Frontend: Vue 3, Vue Router, Vite, TypeScript, and Vitest
- Backend: ASP.NET Core 10, Entity Framework Core, and ASP.NET Core Identity
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
      ItinerarySearch/
      Search/
    Infrastructure/
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
6. For return searches, the user first selects an outbound leg. The backend then returns only compatible inbound options for that selection.
7. Real provider round trips remain distinct from synthetic combinations made from separately bookable one-way fares.

This staged return flow prevents the outbound and inbound result sets from producing an unbounded cross-product in memory. The backend also limits provider calls, caps retained fares per direction, and rejects searches beyond the configured safety limits.

### Multi-destination search

- **Build my route** searches an exact sequence of dated legs, with multiple acceptable airports at every endpoint.
- **Optimize my trip** generates valid destination orders and schedules, prices their flight edges, and ranks complete itineraries.
- Coverage is reported as `exhaustive` when the viable search space was covered or `bounded` when a configured provider-call, state, result, or time limit was reached.
- A bounded result is the best complete set Aveon found within its allowance, not a guarantee of the globally cheapest route.
- Current itineraries use separate one-way bookings and display booking-count, airport-change, and disruption-risk warnings before booking actions.

See the in-product [How search works](https://aveon.lucurlings.nl/how-it-works) page for a user-facing explanation.

## Important Code Areas

### Backend

- Search orchestration: [`SearchService.cs`](apps/backend/Features/Search/SearchService.cs)
- Multi-destination orchestration: [`ItinerarySearchService.cs`](apps/backend/Features/ItinerarySearch/ItinerarySearchService.cs)
- Multi-destination metrics: [`ItinerarySearchTelemetry.cs`](apps/backend/Features/ItinerarySearch/ItinerarySearchTelemetry.cs)
- Bounded priced optimizer: [`OptimizedItinerarySearchRunner.cs`](apps/backend/Features/ItinerarySearch/OptimizedItinerarySearchRunner.cs)
- Search API: [`SearchController.cs`](apps/backend/Features/Search/SearchController.cs)
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

All FlightAPI operations—including airport autocomplete, one-way, round-trip, and multi-destination edge searches—must issue live HTTP requests through the singleton `FlightApiRequestGate`. The configured allowance defaults to five concurrent requests across the whole backend process and can be raised when the FlightAPI subscription permits it. Cache hits bypass the gate, identical concurrent cache misses share one in-flight request, and retries reacquire one permit per live attempt while remaining bounded by the caller cancellation/timeout.

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
| `AVEON_PUBLIC_URL` | Public origin used for canonical metadata, `robots.txt`, and `sitemap.xml` | Required in the container |
| `AVEON_PORT` | Host port for the application container | `8080` |
| `POSTGRES_DB` | PostgreSQL database | `aveon` |
| `POSTGRES_USER` | PostgreSQL username | `aveon` |
| `POSTGRES_PASSWORD` | PostgreSQL password | Development value only |
| `POSTGRES_DEV_PORT` | Local PostgreSQL host port | `5433` |
| `REDIS_DEV_PORT` | Local Redis host port | `6379` |
| `REDIS_FLIGHT_API_ONE_WAY_TTL_MINUTES` | Provider-response cache lifetime | `30` |
| `REDIS_AIRPORT_DATA_TTL_MINUTES` | Airport lookup cache lifetime | `10080` |
| `REDIS_SEARCH_SESSION_TTL_MINUTES` | Search-session lifetime | `30` |
| `SEARCH_ANONYMOUS_MAX_SEARCH_COMBINATIONS` | Guest search limit | `15` |
| `SEARCH_USER_MAX_SEARCH_COMBINATIONS` | Registered-user search limit | `100` |
| `MULTI_DESTINATION_SEARCH_ENABLED` | Independent multi-destination feature flag | `true` |
| `MULTI_DESTINATION_ANONYMOUS_MAX_PROVIDER_CALLS` | Guest multi-destination live-call budget | `25` |
| `MULTI_DESTINATION_USER_MAX_PROVIDER_CALLS` | Registered-user multi-destination live-call budget | `100` |
| `MULTI_DESTINATION_ADMIN_MAX_PROVIDER_CALLS` | Administrator multi-destination live-call budget | `250` |
| `MULTI_DESTINATION_EXECUTION_TIMEOUT_MINUTES` | Ordered and optimized worker timeout | `10` |
| `MULTI_DESTINATION_MAX_ACTIVE_STATES` | Maximum candidate paths retained in an optimizer frontier | `10000` |
| `MULTI_DESTINATION_MAX_EVALUATED_STATES` | Maximum cumulative optimizer path evaluations | `100000` |

`AVEON_PUBLIC_URL` must be an HTTP or HTTPS origin without a path. For the public deployment, use:

```dotenv
AVEON_PUBLIC_URL=https://aveon.lucurlings.nl
```

Development cache lifetimes in `appsettings.Development.json` are intentionally longer than the production defaults.

See [multi-destination rollout and rollback](docs/multi-destination-rollout.md) before changing the feature state. The feature is enabled by default and can be disabled without affecting simple search.

## Observability and Analytics

Backend instruments use the `Aveon.FlightApi` and `Aveon.ItinerarySearch` meters. They report provider concurrency, queued work, cache hits, throttling, active searches, completion status, coverage, duration, result counts, and live provider-call counts.

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

Frontend coverage includes simple and multi-destination search sessions, analytics privacy, stale-request cancellation, pagination, route synchronization, API normalization, authentication races, ranking, filtering, accessibility, date handling, and SEO generation. Backend coverage includes request validation, maximum-input bounds, ordered and optimized orchestration, telemetry, staged returns, pagination, provider-call limiting, user limits, controllers, cache keys, airport lookup, and FlightAPI behavior.

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
- Bounded optimized coverage does not guarantee the globally cheapest possible route.
- Search history and saved itineraries are not currently persisted for users.
- The frontend is a client-rendered Vue application. Static metadata and structured files provide the initial SEO surface, while route metadata is updated in the browser.

## License

See [`LICENSE`](LICENSE).
