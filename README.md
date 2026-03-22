# Aveon

Aveon is a flight metasearch application.

It is not a booking engine. A user search fans out into multiple provider searches, the backend aggregates and normalizes the results, and the frontend presents comparable flight options with provider fare links.

## Stack

- Frontend: Vue 3 + Vite + TypeScript
- Backend: ASP.NET Core Web API
- Cache / session store: Redis
- Flight provider: FlightAPI

## Current Product Scope

- One-way flight search
- Multiple origin airports
- Multiple destination airports
- Flexible departure date range
- Airport autocomplete
- Grouped flight results with multiple fare options
- Session-based search polling with progress updates
- Redis-backed provider response caching

Not implemented yet:

- Booking / checkout
- Authentication
- Database-backed user data
- Return / round-trip search
- Multi-provider aggregation beyond FlightAPI

## Project Structure

```text
apps/
  backend/
    Features/
      Airports/
      Search/
    Infrastructure/
      Caching/
      Models/
      Providers/FlightApi/
  frontend/
    src/
      components/
      features/flight-search/
      api/generated.ts
```

## How Search Works

1. Frontend posts a search request.
2. Backend creates a Redis-backed search session and returns a `searchId`.
3. Backend expands airport/date combinations and queries FlightAPI in the background.
4. Frontend polls the search session endpoint.
5. Partial results accumulate over time.
6. When all combinations complete, polling stops and the progress bar disappears.

## Backend Notes

- Search orchestration lives in [`SearchService.cs`](/Users/lucurlings/Projects/aveon/apps/backend/Features/Search/SearchService.cs).
- Search sessions are stored in Redis via [`RedisSearchSessionStore.cs`](/Users/lucurlings/Projects/aveon/apps/backend/Infrastructure/Caching/RedisSearchSessionStore.cs).
- FlightAPI raw responses are cached at the provider layer via [`RedisProviderResponseCache.cs`](/Users/lucurlings/Projects/aveon/apps/backend/Infrastructure/Caching/RedisProviderResponseCache.cs).
- Provider concurrency is intentionally bounded to protect the FlightAPI plan limits.

## Frontend Notes

- Main page component: [`FlightSearch.vue`](/Users/lucurlings/Projects/aveon/apps/frontend/src/components/FlightSearch.vue)
- Child components:
  - [`FlightSearchBar.vue`](/Users/lucurlings/Projects/aveon/apps/frontend/src/components/flight-search/FlightSearchBar.vue)
  - [`SearchFilters.vue`](/Users/lucurlings/Projects/aveon/apps/frontend/src/components/flight-search/SearchFilters.vue)
  - [`SearchResultCard.vue`](/Users/lucurlings/Projects/aveon/apps/frontend/src/components/flight-search/SearchResultCard.vue)
- Generated API types live in [`generated.ts`](/Users/lucurlings/Projects/aveon/apps/frontend/src/api/generated.ts)
- API normalization for the frontend lives in [`api.ts`](/Users/lucurlings/Projects/aveon/apps/frontend/src/features/flight-search/api.ts)

## Local Development

Prerequisites:

- .NET 10 SDK
- Node.js 22+
- `pnpm`
- Redis

### 1. Install frontend dependencies

```bash
pnpm install
```

### 2. Configure the FlightAPI key

Use .NET user secrets for local backend development:

```bash
cd apps/backend
dotnet user-secrets set "FlightApi:ApiKey" "your-key"
```

### 3. Start Redis

Example with Docker:

```bash
docker run -d --name aveon-redis -p 6379:6379 redis:7
```

### 4. Run the app

From the repo root:

```bash
pnpm dev
```

That starts:

- frontend on `http://localhost:5173`
- backend on the ASP.NET local dev port

If you want to run them separately:

```bash
pnpm dev:frontend
pnpm dev:backend
```

## Type Generation

The frontend types are generated from the backend Swagger document.

With the backend running on `http://127.0.0.1:5200`:

```bash
pnpm --dir apps/frontend generate:types
```

## Caching

Redis is used for three different concerns:

- FlightAPI one-way search cache
- Airport lookup cache
- Search session state for polling

Default production TTLs:

- Flight API one-way cache: 30 minutes
- Airport data cache: 1 week
- Search session cache: 60 minutes

These can be overridden via environment variables in containerized deployment.

## Production Image

A multi-stage Docker image is defined in [`Dockerfile`](/Users/lucurlings/Projects/aveon/Dockerfile).

The production image:

- builds the frontend
- copies the frontend build into backend `wwwroot`
- serves frontend and API from one ASP.NET container

## Docker Compose

[`docker-compose.yml`](/Users/lucurlings/Projects/aveon/docker-compose.yml) runs:

- `aveon` application container
- `aveon-redis` Redis container

Example:

```bash
cp .env.example .env
docker compose up -d
```

Important env vars:

- `FLIGHTAPI_API_KEY`
- `AVEON_PORT`
- `REDIS_FLIGHT_API_ONE_WAY_TTL_MINUTES`
- `REDIS_AIRPORT_DATA_TTL_MINUTES`

## GitHub Container Registry

The repo includes a GitHub Actions workflow at:

- [`.github/workflows/publish-image.yml`](/Users/lucurlings/Projects/aveon/.github/workflows/publish-image.yml)

It builds and publishes the production image to:

```text
ghcr.io/lucurlings/aveon
```

on pushes to `main` and on manual workflow runs.

## Known Constraints

- FlightAPI rate limits still matter even with caching and bounded concurrency.
- Some search combinations can fail at provider level.
- Search sessions currently keep partial successful results even if some combinations fail.
- The current design is optimized for simple metasearch iteration, not for permanent search history.
