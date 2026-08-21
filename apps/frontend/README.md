# Aveon frontend

This package contains Aveon's Vue 3 client. It is a client-rendered TypeScript application built with Vite and tested with Vitest and Vue Test Utils.

Repository-wide setup, backend configuration, deployment, and product behavior are documented in the root [`README.md`](../../README.md). The multi-destination and Explore implementation contracts live in [`docs/multi-destination-search-plan.md`](../../docs/multi-destination-search-plan.md) and [`docs/explore-routes-plan.md`](../../docs/explore-routes-plan.md).

## Product routes

- `/` — product overview with a fast cached random-hub direct-route globe and a page-one preview while a completely cold cache warms in the background.
- `/search` — flexible one-way and return search across airport groups and dates.
- `/explore` — interactive direct-route discovery with onward-path building.
- `/build-route` — ordered Build my route flow with dated flight legs.
- `/optimize-trip` — bounded Optimize my trip flow for comparing complete journeys.
- `/how-it-works` — user-facing search and coverage explanation.
- `/about` — product scope and metasearch limitations.

Shared root URLs containing legacy search query parameters are redirected to `/search` while preserving their state. Legacy `/multi-destination` URLs redirect to the matching standalone multi-route page.

## Main frontend areas

```text
src/
  components/
    flight-search/       Simple-search controls, filters, progress, and results
  features/
    auth/                Cookie-authenticated account state
    explore/             Explore API, contracts, and reusable globe
    flight-search/       Search state, API normalization, dates, filters, and URLs
    itinerary-search/    Ordered and optimized multi-destination flows
  pages/                 Route-level product pages
  api/generated.ts       OpenAPI-generated backend contracts
  router.ts              Lazy routes and route metadata
  seo.ts                 Canonical metadata and structured data
tests/unit/              Component and feature tests
config/seoFiles.ts       Build-time robots.txt and sitemap generation
```

## Explore visualization

`RouteGlobe.vue` lazy-loads Globe.gl and uses locally bundled World Atlas TopoJSON data. It supports destination markers, animated and solid route arcs, hover/focus emphasis, camera movement, committed-path display, responsive fixed heights, reduced-motion behavior, WebGL cleanup, and a non-WebGL fallback. The homepage reuses it in preview mode with zoom and destination selection disabled.

The Explore page reuses the normal airport autocomplete, requires an exact first leave date, keeps URL-backed committed paths, and only navigates to Search or Build my route after an explicit action. Search receives that exact date. Build my route receives only the first date, leaves later dates for the traveler, and warns that onward route suggestions may not operate or return fares. Explore itself never requests prices, and neither prefill starts provider calls automatically.

## Development

From the repository root, install dependencies and start the complete development stack:

```bash
pnpm install
pnpm dev
```

To run only the frontend:

```bash
pnpm --dir apps/frontend dev
```

Vite serves on `http://localhost:5173` when available and proxies `/api` to `http://localhost:5210` by default. Override the backend target with `VITE_DEV_API_TARGET`.

## Validation

Run the frontend test suite:

```bash
pnpm --dir apps/frontend test
```

Run type checking and the production build:

```bash
AVEON_PUBLIC_URL=https://aveon.lucurlings.nl pnpm --dir apps/frontend build
```

Tests cover simple and multi-destination searches, Explore and globe behavior, route history and prefill handoffs, filtering and rankings, return-price clarity, authentication state, accessibility, responsive contracts, reduced motion, API normalization, SEO, and WebGL fallback.

## API type generation

With the backend and Swagger running on `http://localhost:5210`, regenerate the frontend contracts with:

```bash
pnpm --dir apps/frontend generate:types
```

Review the generated diff, then rerun the frontend tests and production build.
