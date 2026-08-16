# Explore Routes and Homepage Globe

Status: Phase one and the phase-two durable monthly airport catalogue are implemented and covered by automated tests and live smoke validation on 2026-08-08. The catalogue migration, real 7,884-row upstream import, failed-refresh retention path, exact-date Explore, rolling onward Explore, and homepage hero were validated against real infrastructure. The GitHub file-revision query and exact commit-pinned raw download contract were live-verified on 2026-08-09. This document remains the source of truth for the Explore Routes page and homepage globe.

Update this document before implementation whenever a product decision, provider constraint, cache policy, public contract, or acceptance criterion changes.

## Phase-one completion

- [x] Schedule v1 rolling-network client, Schedule v2 exact-date client, normalization, defensive pagination, and Explore/hero API contracts.
- [x] Weekly Explore and monthly hero Redis freshness profiles with longer stale retention, stale-if-error, and coalesced refreshes.
- [x] Schedule traffic uses the same process-wide FlightAPI concurrency gate as autocomplete, simple fares, and multi-destination calls.
- [x] Reusable lazy-loaded globe with routes, markers, interaction, reduced-motion handling, cleanup, and a non-WebGL fallback.
- [x] Dedicated Explore page with shared airport autocomplete, destination filtering, random spotlight, accessible list, and Search handoff.
- [x] Required first leave date bounded from today through 365 days ahead, exact-date first-leg filtering, date-aware cache keys, and date-preserving Search/Multi-destination handoffs without Explore fare calls.
- [x] Homepage preview globe, navigation/footer/discovery links, lazy route, metadata, and sitemap coverage.
- [x] Search prefill hydrates airports and dates without starting a fare request until the traveler confirms.
- [x] Backend and frontend acceptance tests, production frontend build, Compose validation, README, and environment documentation.
- [x] Transient schedule retry, partial-page retention, safe gateway errors, and cache/live-call diagnostics.
- [x] Immediate hover animation restart, solid active-route underlay, destination camera movement, stable map sizing, and animated state changes without flicker.

## Phase two — durable airport catalogue

### Decision

Use PostgreSQL as the authoritative airport metadata catalogue and Redis only for volatile FlightAPI schedule networks.

- PostgreSQL stores durable airport facts: IATA and ICAO codes, airport name, city, subdivision, country, latitude, longitude, elevation, and timezone.
- Redis stores the observed schedule result for an origin and date/profile as destination IATA codes plus schedule completeness and observation metadata.
- Every Explore response enriches the cached destination codes with one batched PostgreSQL lookup. Do not cache airport coordinates or names inside the schedule entry.
- Schedule v2 remains the exact-date source for the first Explore leg. Schedule v1 remains the rolling source for homepage and onward route discovery, but it supplies route codes only; it is no longer the coordinate/name fallback.
- No fare or price requests are introduced by this phase.

This separation lets the monthly catalogue update correct names or coordinates immediately without invalidating schedule caches, while Redis continues to absorb expensive provider schedule traffic.

### Dataset and licensing

Use [`mborsetti/airportsdata`](https://github.com/mborsetti/airportsdata) and its [`airports.csv`](https://github.com/mborsetti/airportsdata/blob/main/airportsdata/airports.csv) as the catalogue source. The source columns are `icao`, `iata`, `name`, `city`, `subd`, `country`, `elevation`, `lat`, `lon`, `tz`, and `lid`.

- Import only records with a valid, non-empty three-letter IATA code and usable latitude/longitude.
- Normalize IATA and ICAO codes to uppercase and trim textual fields.
- Treat the IATA code as the live catalogue's stable primary key.
- Preserve the upstream MIT copyright and permission notice in `THIRD_PARTY_NOTICES.md` and document the dataset source in the repository README.
- Use GitHub's commits endpoint filtered to `airportsdata/airports.csv` to obtain the latest commit that changed the file. Store that commit SHA as the authoritative source revision.
- Download changed data from a raw URL pinned to that exact commit SHA so the checked revision and imported bytes cannot drift if the default branch moves between requests.
- Record the downloaded content's SHA-256 checksum as a second integrity identity. A missing or invalid GitHub revision response is a failed refresh; retain the previous catalogue rather than downloading an unpinned moving-branch version.
- The dataset is best-effort reference data. Missing airports must degrade safely; the application must never invent coordinates.

### PostgreSQL model

Add an `airports` table containing:

- `iata` primary key.
- `icao`, `name`, `city`, `subdivision`, `country_code`.
- `latitude`, `longitude`, `elevation_feet`, `timezone`.
- `source_updated_at` for the catalogue generation that supplied the row.

Add a singleton `airport_catalog_metadata` record containing:

- Source name and source URL.
- Authoritative Git commit SHA and downloaded-content SHA-256 checksum.
- Successful import timestamp and imported row count.
- The last attempted refresh timestamp and a safe failure summary for operational diagnosis.

Create both tables through the existing EF Core migration path. Expose catalogue access through a focused repository that supports one airport lookup and a single batched lookup for a set of IATA codes. Avoid one database query per destination.

### Monthly refresh lifecycle

Run the catalogue updater in the backend as a hosted maintenance service:

1. Check at startup, then periodically, whether the last successful upstream confirmation is at least thirty days old. A recent catalogue skips all upstream calls only when it has a valid stored Git commit SHA and its live row count and configured required hubs still match the recorded snapshot. Missing or legacy revision metadata bootstraps the new SHA tracking immediately; failed integrity forces a guarded repair.
2. Acquire a PostgreSQL advisory lock before refreshing so only one process imports, including after horizontal scaling is introduced. While holding that lock, remove any staging batches abandoned by an earlier hard process termination.
3. Query GitHub for the latest commit affecting `airportsdata/airports.csv`. If its SHA matches the stored source revision and the live catalogue is intact, record a successful unchanged confirmation and stop without downloading the CSV.
4. When the SHA changed, no catalogue exists, or live integrity failed, download the CSV from the raw URL pinned to that exact SHA. Bound both the revision lookup and CSV download by timeout and response-size limits. Do not alter the live table while downloading or parsing.
5. Parse the complete CSV using a real CSV parser that handles quoted commas and UTF-8 data, rejects malformed quoting, and enforces the destination schema's text-length boundaries. Normalize and validate every candidate row before database replacement.
6. If a changed revision produces the same content checksum, record the new revision and successful confirmation without replacing the live rows. Otherwise, load validated rows into a staging table and run integrity checks.
7. In one database transaction, replace the live catalogue from staging and update catalogue metadata. PostgreSQL's transactional replacement must keep the previous catalogue visible until commit.
8. On revision lookup, download timeout, parse, validation, staging, or transaction failure, retain the previous live catalogue unchanged, record/log the failure, and retry on a later maintenance check. Caller-requested cancellation propagates without being misreported as a source failure.
9. Release the advisory lock in all success, failure, timeout, and cancellation paths.

Never clear or truncate the live table before a complete replacement has passed validation. The import is allowed to replace the catalogue only when all of these checks pass:

- No duplicate normalized IATA primary keys.
- Every row has finite coordinates within latitude `-90..90` and longitude `-180..180`.
- Required text fields and country codes satisfy the chosen schema constraints.
- The imported count is plausible and does not unexpectedly fall by more than ten percent from the previous successful import without an explicit operator override.
- A configured set of required major hubs, including every homepage hub, is present.
- The source is non-empty, the checksum is calculated from the exact downloaded bytes before parsing, and the staging row count matches the validated in-memory import set.

The updater must support a manually invoked refresh for operational recovery and testing. A forced refresh bypasses freshness, unchanged-revision, and unchanged-checksum no-op shortcuts so it always downloads the commit-pinned CSV, validates it, and performs the guarded staging and transactional replacement. It does not bypass source validation or import safety checks.

### Explore cache and service refactor

- [x] Replace cached `ExploreRoutesResponse` objects with an internal code-only schedule cache entry containing origin code, destination codes, fetch/observation timestamps, completeness, and stale state inputs.
- [x] Preserve the existing cache profiles and stale-if-error behavior: exact Explore results are fresh for seven days, homepage/rolling results for thirty days, with their longer configured retention windows.
- [x] Keep cache keys partitioned by profile, origin, and exact departure date or `rolling`.
- [x] After reading or refreshing a schedule entry, batch-load the origin and all destination metadata from PostgreSQL and build the public `ExploreRoutesResponse`.
- [x] Omit schedule codes absent from the catalogue, log their codes, and mark the public result incomplete. Do not fail an otherwise useful network or synthesize airport data.
- [x] Refactor exact-date Schedule v2 discovery to use PostgreSQL metadata instead of first fetching a Schedule v1 metadata network.
- [x] Refactor Schedule v1 homepage and onward discovery to retain only normalized destination codes from provider responses and enrich them from PostgreSQL.
- [x] Preserve request coalescing, defensive pagination, partial-page retention, transient retry, stale fallback, safe `502` responses, diagnostics, and the process-wide FlightAPI concurrency gate. Coalesced callers have independent cancellation: one canceled waiter does not abort shared work needed by another, while the shared provider operation is canceled when its final waiter leaves.
- [x] Ensure a successful catalogue refresh becomes visible on the next response without deleting Redis schedule entries.

### Implementation checklist

- [x] Add airport catalogue and import-metadata entities, mappings, indexes, migration, and repository.
- [x] Add typed catalogue refresh options with safe defaults for the GitHub revision URL, commit-pinned raw URL template, thirty-day refresh age, check interval, timeout, maximum response size, minimum/relative row-count checks, and required hub codes.
- [x] Add the GitHub file-revision lookup, commit-pinned CSV downloader, parser, validator, checksum calculation, staging loader, transactional replacement, and PostgreSQL advisory lock.
- [x] Verify live row-count/required-hub integrity before trusting freshness or an unchanged checksum, and remove abandoned staging batches under the refresh lock.
- [x] Add the startup/periodic hosted maintenance service and a manual refresh entry point that share the same importer.
- [x] Seed or perform the first import safely when no catalogue exists; return a service-unavailable Explore response with a useful internal log if enrichment is impossible rather than falling back to fabricated coordinates.
- [x] Change Redis Explore values to code-only schedule entries and refactor both Schedule v1 and v2 flows to use catalogue enrichment.
- [x] Add catalogue freshness, import duration, row count, rejected-row, refresh failure, missing-code, and schedule-cache hit/miss diagnostics without logging API keys or response bodies.
- [x] Add `THIRD_PARTY_NOTICES.md`, README configuration/operations guidance, deployment notes, and catalogue recovery instructions.
- [x] Keep the public route-response contract backward compatible and regenerate frontend API types for the new admin catalogue-refresh endpoint.

### Verification and acceptance criteria

- [x] Parser tests cover quoted commas, Unicode, blank optional fields, normalization, invalid/duplicate IATA codes, invalid coordinates, malformed quoting, and database text-length boundaries.
- [x] Import validation tests cover missing required hubs, implausibly small or greater-than-ten-percent catalogue drops (including non-round previous counts with an exact ceiling boundary), checksum handling, unchanged-revision behavior, and a changed revision with unchanged content.
- [x] Refresh regression tests prove an unchanged commit SHA skips the CSV download, a changed revision or missing stored revision downloads from the exact pinned URL, missing/invalid upstream revisions fail safely without an unpinned download, damaged live data is repaired even when the SHA is unchanged, revision lookup/download failures retain the previous catalogue, caller cancellation remains distinct from an internal timeout, and abandoned staging rows are removed.
- [x] PostgreSQL-backed CI tests prove a successful staged import replaces the catalogue atomically, an injected live-table insert failure after the transactional delete restores the previous live rows, a separate injected commit failure after the live rows and metadata have both been written rolls both changes back, and cancellation before promotion leaves the previous catalogue intact.
- [x] PostgreSQL-backed and unit locking tests prove concurrent refresh attempts result in at most one importer and that the lock is released after success, failure, and cancellation/timeout.
- [x] Scheduling tests cover initial import, skipping a catalogue newer than thirty days, due refresh, failed refresh, a forced refresh of a recent catalogue, and forced replacement when the source SHA and checksum are unchanged; controller tests prove the Admin-protected manual endpoint invokes that same service with `force: true`.
- [x] Explore service tests prove v1 and v2 destination codes are batch-enriched from PostgreSQL, no v1 metadata request is made for an exact-date v2 result, and missing catalogue entries are omitted and mark the response incomplete.
- [x] Redis tests prove schedule entries contain codes and schedule metadata only, preserve current freshness/retention/stale behavior, and survive a catalogue refresh without invalidation.
- [x] Regression tests prove Explore still makes no fare calls and all live Schedule API requests still share the process-wide FlightAPI concurrency limit.
- [x] An integration-style service test refreshes catalogue metadata while a schedule network remains cached and observes the updated airport details on the next Explore response.
- [x] Backend test suite, frontend test suite, production frontend build, migration validation, and Compose validation pass.
- [x] Operational smoke test confirms exact-date Explore, rolling onward Explore, and the homepage globe against live FlightAPI data. On 2026-08-08, exact-date DUB returned HTTP 200 with 149 destinations, rolling DUB returned HTTP 200 with 163 complete destinations, and the randomly selected DXB hero returned HTTP 200 with 214 destinations. DUB was partial because FlightAPI returned legacy `KIV`, and DXB was partial because it returned legacy `ULH`; both were safely omitted because the maintained catalogue has no matching metadata.

Phase-two implementation and live validation are complete. The old Schedule v1 coordinate-cache workaround is removed; provider codes absent from the maintained catalogue remain explicitly partial rather than receiving invented coordinates.

## Onward route discovery — completed increment

- [x] Selecting a destination pins and highlights that leg without navigating away.
- [x] A one-leg selection offers `Search fares` and `Explore onward from {airport}` actions.
- [x] Exploring onward preserves the committed route, makes the selected destination the active origin, and loads its direct network.
- [x] Selecting another destination builds a multi-leg route preview with explicit `Continue in Build my route` and `Keep exploring` actions.
- [x] Explore paths are reflected in the URL and support browser Back, route breadcrumbs, deselection, and removing the last stop.
- [x] Build my route accepts an ordered airport-chain prefill without automatically starting a search.
- [x] Hover, focus, selection, route-chain, responsive, and accessible keyboard behavior are covered by tests.

## Current Explore behavior

Add a dedicated Explore page where a traveler selects one starting airport and leave date and sees direct destinations scheduled that day on an interactive globe. Selecting a destination highlights that direct leg and presents the appropriate handoff instead of navigating immediately. A one-leg route can continue to normal Search with that date prefilled, while an onward chain uses rolling route-network suggestions and can be handed to Build my route with its airport order and first date prefilled.

Replace the homepage's existing journey illustration with a half-width, slowly rotating preview globe using a random major hub. FlightAPI Schedule API v1 supplies rolling route codes for the homepage and onward networks, while Schedule API v2 supplies exact-date first-leg route codes. Both flows enrich airport names and coordinates from the monthly PostgreSQL catalogue. The deployment defaults to a shared five-request concurrency allowance, which can be raised when the configured provider subscription permits it.

References:

- [Airport Schedule API v1 documentation](https://www.flightapi.io/documentation/airport-schedule-api/)
- [Airport Schedule API v2 documentation](https://www.flightapi.io/documentation/airport-schedule-api-v2/)
- [FlightAPI Airport Schedule plans](https://www.flightapi.io/airport-schedule-api/)
- [`mborsetti/airportsdata`](https://github.com/mborsetti/airportsdata)

## Implemented Explore experience

### Route-network backend

- Add `GET /api/v1/explore/routes?origin=DUB&departureDate=2026-09-18` for exact-date first-leg discovery, retain the date-optional form for rolling onward discovery, and use `GET /api/v1/explore/hero` for the homepage.
- Use Airport Schedule API v2 in departures mode for the requested first leave date. Fetch its four documented pages defensively and deduplicate the returned destination codes.
- Use Airport Schedule API v1 without a specific day for homepage previews and undated onward route suggestions, retrieving every reported page from its rolling schedule window and retaining normalized route codes only.
- Enrich v1 and v2 codes with one batched PostgreSQL catalogue lookup. Omit destinations that cannot be located safely and mark the response partial; never synthesize coordinates or perform one metadata query per destination.
- Derive `observedFrom` and `observedTo` as five days before and after the fetch date. FlightAPI documents this eleven-day range for v1 requests that omit `day`; the response does not provide separate window fields.
- Normalize and deduplicate codeshare flights into one destination per IATA code.
- Return:
  - Origin code, name, city, country, latitude, and longitude.
  - Destination code, name, city, country, latitude, and longitude.
  - Observation-window and cache timestamps.
  - `isComplete` and `isStale` indicators.
- Validate origins as three-letter IATA codes, accept exact dates only from today through 365 days ahead, reject malformed or mismatched page-one schedule envelopes, and cap unexpected provider pagination defensively.
- Put every live schedule-page request through `FlightApiClient`, after cache lookup, so schedule, autocomplete, fare, and multi-destination traffic all share the existing process-wide FlightAPI concurrency gate.
- Coalesce identical cache misses to prevent multiple visitors from refreshing the same airport simultaneously. Each visitor may cancel independently; keep the shared refresh alive while any waiter remains and cancel it after the last waiter leaves.
- Treat page one as required, but preserve successful destinations and mark the response incomplete when a later page fails.
- Retry only FlightAPI's specific generic transient schedule `400` response; do not retry ordinary provider validation errors.
- Convert exhausted provider HTTP responses, transport failures, internal timeouts, and response-deserialization failures into a safe `502` problem response instead of exposing a developer exception. Preserve caller-requested cancellation and do not disguise unrelated application or database failures as provider outages.
- Log route-network cache hits, misses, stale refreshes, and each genuine live schedule page distinctly.
- Add code-only Redis route-network caches keyed by schema version, profile, origin, and either the exact departure date or `rolling`:
  - Explore results: seven-day freshness.
  - Homepage hub results: thirty-day freshness.
  - Retain stale values longer and serve them when FlightAPI temporarily fails; never replace a complete cache entry with a partial page set.
- Resolve homepage previews from a randomly selected fresh retained hub whenever possible instead of blocking the index on a different cold hub. Queue the originally selected cold or stale hub for sequential background warming so the curated set continues to rotate without multiple hub refresh jobs fanning out concurrently.
- When no homepage hub has ever been cached, fetch and display page one as an explicitly incomplete quick preview, then warm the complete multi-page network in the background. Never store that incomplete preview as the durable hero cache.
- Configure TTLs through `Redis` options rather than hardcoding them.
- Select homepage origins server-side from `DUB`, `AMS`, `LHR`, `CDG`, `FRA`, `JFK`, `ATL`, `DXB`, `DOH`, `SIN`, `HND`, and `SYD`.

### Globe and Explore experience

- Add a lazy-loaded `/explore` route and links in the main navigation, footer, homepage actions, and discovery-mode section.
- Add `globe.gl` as the visualization engine and render a stylized globe with locally bundled Natural Earth 110m country topology, material, atmosphere, and graticules so rendering does not depend on third-party image requests.
- Build one reusable globe component for Explore and the homepage:
  - Origin marker, destination markers, and curved origin-to-destination arcs.
  - Drag and zoom controls.
  - Slow automatic rotation.
  - Pause rotation during user interaction.
  - A fading map-loading overlay before WebGL is ready.
  - Disable rotation and arc animation for reduced-motion users.
  - Responsive sizing and cleanup of WebGL resources on unmount.
- Explore page:
  - Reuse the existing airport autocomplete with a one-airport limit.
  - Require one leave date and load the exact-date first-leg globe after an airport is selected.
  - Include loading, empty, stale-data, provider-error, retry, and WebGL-fallback states.
  - Provide a searchable, list-based destination alternative for keyboard and screen-reader users.
  - Treat destination marker and list-item activation as selection, not navigation. The selected destination and its direct leg remain highlighted until changed or cleared.
  - On hover or keyboard focus, emphasize only the prospective current leg and dim the active origin's other arcs. Previously committed route legs remain visible.
  - Restart the active route animation immediately, retain a faint solid origin-to-destination line beneath it, pause rotation, and move the camera toward the hovered airport.
  - Make `Surprise me` rotate to and select one reachable destination, using the same actions as a manual selection.
  - Place the selected-route summary and explicit actions above the globe so they are visible without scrolling through the map.
  - Use fixed responsive globe heights to prevent selection-card layout from feeding back into WebGL resizing.
  - Animate loading, errors, result replacement, route breadcrumbs, selection cards, and destination filtering while retaining and dimming the previous network during refresh.
  - Explain that the first network is scheduled for the selected date, Explore does not fetch prices, and onward networks are undated route suggestions that still require date and fare validation.
- Homepage:
  - Replace the current right-side journey illustration with the random-hub globe.
  - Keep it as a preview: center the wide overview camera on the displayed source airport, allow drag and slow rotation, disable zoom so wheel input scrolls the page, and reserve destination selection for Explore.
  - Show the selected hub and destination count with an `Explore routes` call to action.
  - Fall back to a styled static globe and call to action if data or WebGL is unavailable.

### Search handoff

- For a route containing exactly one leg, show `Search fares` and navigate only when that button is pressed, using the existing `origins`, `destinations`, and `dates` query parameters plus a one-time prefill flag.
- Update search-route hydration so the selected airports and exact Explore leave date appear, but no fare request starts automatically.
- Remove the prefill flag when the traveler presses the normal Search flights button; subsequent URL sharing and reload behavior remains unchanged.

### Onward exploration and ordered-route handoff

- Maintain two separate pieces of state:
  - The committed path, beginning with the starting airport and containing every airport accepted through `Explore onward`.
  - One selected candidate destination from the active origin. Selection previews the next leg but does not commit it or navigate.
- After the first destination is selected, show:
  - `Search fares` for the selected one-leg route.
  - `Explore onward from {destination}` to append that airport to the committed path and load its direct network.
  - `Clear selection` to return to the complete network from the active origin.
- After at least one leg has already been committed and another destination is selected, show the complete preview such as `DUB → AMS → JFK` and replace the simple-search primary action with:
  - `Continue in Build my route`, which opens the ordered mode of Multi-destination with the entire preview path prefilled when it is within the ordered-leg limit.
  - `Keep exploring from {destination}`, which commits the selected candidate and loads its network.
  - `Clear selection` and `Remove last stop` recovery actions.
- Do not redirect automatically when any destination is selected. Every Search or Multi-destination transition requires an explicit labeled button.
- Display committed airports as a breadcrumb/journey tray. Selecting an earlier breadcrumb truncates the path to that airport and reloads its network; browser Back restores the previous committed path.
- Persist committed Explore state as `/explore?path=DUB,AMS&date=2026-09-18`. The date belongs only to the first leg; the final code is the active origin and an uncommitted selected candidate does not enter the URL.
- When hydrating a shared path, verify that every next airport appears in the preceding airport's returned direct destinations. Reject invalid, stale, or manually fabricated edges instead of presenting them as observed routes.
- Hand ordered paths to `/multi-destination?mode=ordered&route=DUB,AMS,JFK&departureDate=2026-09-18&source=explore&prefill=true`.
- Add Multi-destination route hydration that:
  - Opens `Build my route`, which remains the default mode.
  - Creates one ordered leg for each adjacent airport pair.
  - Prefills only the first leg with the Explore leave date and leaves later dates empty for the traveler.
  - Shows a prominent warning that onward suggestions were not checked for those later dates and may not operate or return fares.
  - Does not start provider calls until the traveler reviews dates and presses `Search complete route`.
  - Removes the one-time prefill flag when the traveler submits or materially edits the generated route.
- Respect the same maximum ordered-leg limit as Build my route. Disable `Keep exploring` at the limit while retaining `Continue in Build my route`; because the extra candidate cannot fit, that action hands off the already committed path and explicitly tells the traveler that the preview candidate is excluded.
- Keep the distinction explicit: Explore proves only that each adjacent scheduled edge was observed independently. It does not prove date compatibility, connection protection, fare availability, visa eligibility, or a through-ticket.
- Reuse the date-optional rolling form of `GET /api/v1/explore/routes` for every onward airport. Weekly caching, miss coalescing, stale fallback, defensive pagination, and the shared FlightAPI gate apply unchanged.

## Implemented verification

- [x] Backend tests cover v1 and v2 request paths, exact-date filtering, shared-gate use, multi-page aggregation, codeshare deduplication, catalogue enrichment and missing codes, invalid origins, empty schedules, later-page failure, cache profiles, stale fallback, cache diagnostics, transient retry, safe `502` responses for response/transport/timeout/deserialization failures, caller cancellation, request coalescing, leader/follower cancellation isolation, and final-waiter shared-work cancellation.
- [x] Mixed concurrency tests combine schedule pages, airport autocomplete, simple fare searches, and multi-destination calls and assert that live FlightAPI concurrency never exceeds the configured shared limit.
- [x] API contract tests cover Explore and homepage responses, including complete and stale metadata.
- [x] Frontend tests cover airport selection, globe loading, errors, empty results, destination highlighting, explicit Search action, `Surprise me`, deterministic browser-local date formatting across a UTC calendar boundary, and prefilled Search not auto-starting.
- [x] Onward-exploration tests cover hover/focus arc isolation, committed-leg preservation, candidate replacement and clearing, breadcrumb truncation, browser Back restoration, repeated-airport handling, maximum-leg enforcement, and stale/error states after changing the active origin.
- [x] Multi-destination hydration tests cover adjacent-leg generation, first-date-only Explore handoff, blank onward dates, the availability warning, ordered-mode selection, edit/submit prefill removal, and no automatic provider request.
- [x] Homepage and globe tests cover cached-hub fallback, cold-cache page-one preview and background warming, continued random-hub rotation, source-centered overview framing, the Explore call to action, lazy loading, animation timing, hover camera movement, reduced motion, stable responsive sizing, cleanup, and WebGL fallback.
- [x] Accessibility tests cover keyboard destination selection, the screen-reader destination list, progress announcements, and non-canvas fallback content.
- [x] The production frontend build and complete automated backend and frontend suites pass.

## Assumptions and deferred work

- Interactive first-leg discovery shows direct destinations returned by FlightAPI v2 for the selected leave date when PostgreSQL catalogue metadata is available; the homepage and onward suggestions use v1's rolling network enriched from the same catalogue.
- Explore does not call FlightAPI's fare endpoints or display price previews. Search remains the only authority for fare availability.
- A scheduled route does not imply current fare availability; Search remains authoritative.
- Destination selection never redirects immediately. `Surprise me` and manual selection share the same explicit Search, onward-exploration, and Multi-destination actions.
- Repeating an airport already present in the committed path is rejected to prevent accidental cycles in phase one. Returning to the starting airport remains available later through Build my route's existing `Return to starting point` control.
- The homepage globe is a preview and does not redirect when a destination is clicked.
