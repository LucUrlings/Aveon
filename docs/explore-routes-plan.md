# Explore Routes and Homepage Globe — Phase One

Status: Phase one, onward route discovery, provider resilience, and interaction polish are implemented and covered by automated tests on 2026-08-02. A final end-to-end smoke test against live FlightAPI schedule data remains operational validation rather than deferred implementation. This document remains the source of truth for the Explore Routes page and homepage globe.

Update this document before implementation whenever a product decision, provider constraint, cache policy, public contract, or acceptance criterion changes.

## Phase-one completion

- [x] Schedule v1 client, normalization, defensive pagination, and Explore/hero API contracts.
- [x] Weekly Explore and monthly hero Redis freshness profiles with longer stale retention, stale-if-error, and coalesced refreshes.
- [x] Schedule traffic uses the same process-wide FlightAPI concurrency gate as autocomplete, simple fares, and multi-destination calls.
- [x] Reusable lazy-loaded globe with routes, markers, interaction, reduced-motion handling, cleanup, and a non-WebGL fallback.
- [x] Dedicated Explore page with shared airport autocomplete, destination filtering, random spotlight, accessible list, and Search handoff.
- [x] Homepage preview globe, navigation/footer/discovery links, lazy route, metadata, and sitemap coverage.
- [x] Search prefill hydrates airports and dates without starting a fare request until the traveler confirms.
- [x] Backend and frontend acceptance tests, production frontend build, Compose validation, README, and environment documentation.
- [x] Transient schedule retry, partial-page retention, safe gateway errors, and cache/live-call diagnostics.
- [x] Immediate hover animation restart, solid active-route underlay, destination camera movement, stable map sizing, and animated state changes without flicker.
- [ ] Operational smoke test against live FlightAPI schedule data; no implementation code is deferred by this item.

## Onward route discovery — completed increment

- [x] Selecting a destination pins and highlights that leg without navigating away.
- [x] A one-leg selection offers `Search fares` and `Explore onward from {airport}` actions.
- [x] Exploring onward preserves the committed route, makes the selected destination the active origin, and loads its direct network.
- [x] Selecting another destination builds a multi-leg route preview with explicit `Continue in Build my route` and `Keep exploring` actions.
- [x] Explore paths are reflected in the URL and support browser Back, route breadcrumbs, deselection, and removing the last stop.
- [x] Build my route accepts an ordered airport-chain prefill without automatically starting a search.
- [x] Hover, focus, selection, route-chain, responsive, and accessible keyboard behavior are covered by tests.

## Summary

Add a dedicated Explore page where a traveler selects one starting airport and sees its current direct destinations on an interactive globe. Selecting a destination highlights that direct leg and presents the appropriate handoff instead of navigating immediately. A one-leg route can continue to normal Search, while an onward chain can be explored further and handed to Build my route with its airport order prefilled.

Replace the homepage's existing journey illustration with a half-width, slowly rotating preview globe using a random major hub. FlightAPI Schedule API v1 is used because its response includes destination coordinates and pagination. The deployment defaults to a shared five-request concurrency allowance, which can be raised when the configured provider subscription permits it.

References:

- [Airport Schedule API v1 documentation](https://www.flightapi.io/documentation/airport-schedule-api/)
- [Airport Schedule API v2 documentation](https://www.flightapi.io/documentation/airport-schedule-api-v2/)
- [FlightAPI Airport Schedule plans](https://www.flightapi.io/airport-schedule-api/)

## Implementation changes

### Route-network backend

- Add `GET /api/v1/explore/routes?origin=DUB` for the dedicated page and `GET /api/v1/explore/hero` for the homepage.
- Use Airport Schedule API v1 in departures mode without a specific day, retrieving every reported page from its rolling schedule window.
- Derive `observedFrom` and `observedTo` as five days before and after the fetch date. FlightAPI documents this eleven-day range for v1 requests that omit `day`; the response does not provide separate window fields.
- Normalize and deduplicate codeshare flights into one destination per IATA code.
- Return:
  - Origin code, name, city, country, latitude, and longitude.
  - Destination code, name, city, country, latitude, and longitude.
  - Observation-window and cache timestamps.
  - `isComplete` and `isStale` indicators.
- Validate origins as three-letter IATA codes and cap unexpected provider pagination defensively.
- Put every live schedule-page request through `FlightApiClient`, after cache lookup, so schedule, autocomplete, fare, and multi-destination traffic all share the existing process-wide FlightAPI concurrency gate.
- Coalesce identical cache misses to prevent multiple visitors from refreshing the same airport simultaneously.
- Treat page one as required, but preserve successful destinations and mark the response incomplete when a later page fails.
- Retry only FlightAPI's specific generic transient schedule `400` response; do not retry ordinary provider validation errors.
- Convert exhausted provider schedule failures into a safe `502` problem response instead of exposing a developer exception.
- Log route-network cache hits, misses, stale refreshes, and each genuine live schedule page distinctly.
- Add Redis route-network caches:
  - Explore results: seven-day freshness.
  - Homepage hub results: thirty-day freshness.
  - Retain stale values longer and serve them when FlightAPI temporarily fails; never replace a complete cache entry with a partial page set.
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
  - Load the globe after an airport is selected.
  - Include loading, empty, stale-data, provider-error, retry, and WebGL-fallback states.
  - Provide a searchable, list-based destination alternative for keyboard and screen-reader users.
  - Treat destination marker and list-item activation as selection, not navigation. The selected destination and its direct leg remain highlighted until changed or cleared.
  - On hover or keyboard focus, emphasize only the prospective current leg and dim the active origin's other arcs. Previously committed route legs remain visible.
  - Restart the active route animation immediately, retain a faint solid origin-to-destination line beneath it, pause rotation, and move the camera toward the hovered airport.
  - Make `Surprise me` rotate to and select one reachable destination, using the same actions as a manual selection.
  - Place the selected-route summary and explicit actions above the globe so they are visible without scrolling through the map.
  - Use fixed responsive globe heights to prevent selection-card layout from feeding back into WebGL resizing.
  - Animate loading, errors, result replacement, route breadcrumbs, selection cards, and destination filtering while retaining and dimming the previous network during refresh.
  - Explain that routes are direct destinations observed in the current schedule window, not guaranteed fares or every seasonal service.
- Homepage:
  - Replace the current right-side journey illustration with the random-hub globe.
  - Keep it as a preview: drag and slow rotation are allowed, zoom is disabled so wheel input scrolls the page, and destination selection is reserved for Explore.
  - Show the selected hub and destination count with an `Explore routes` call to action.
  - Fall back to a styled static globe and call to action if data or WebGL is unavailable.

### Search handoff

- For a route containing exactly one leg, show `Search fares` and navigate only when that button is pressed, using the existing `origins` and `destinations` query parameters plus a one-time prefill flag.
- Update search-route hydration so the selected airports and existing default dates appear, but no fare request starts automatically.
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
- Persist committed Explore state as `/explore?path=DUB,AMS`. The final code is the active origin; an uncommitted selected candidate does not enter the URL.
- When hydrating a shared path, verify that every next airport appears in the preceding airport's returned direct destinations. Reject invalid, stale, or manually fabricated edges instead of presenting them as observed routes.
- Hand ordered paths to `/multi-destination?mode=ordered&route=DUB,AMS,JFK&prefill=true`.
- Add Multi-destination route hydration that:
  - Opens `Build my route`, which remains the default mode.
  - Creates one ordered leg for each adjacent airport pair.
  - Uses the existing default-date progression for the generated legs.
  - Does not start provider calls until the traveler reviews dates and presses `Search complete route`.
  - Removes the one-time prefill flag when the traveler submits or materially edits the generated route.
- Respect the same maximum ordered-leg limit as Build my route. Disable `Keep exploring` at the limit while retaining `Continue in Build my route`; because the extra candidate cannot fit, that action hands off the already committed path and explicitly tells the traveler that the preview candidate is excluded.
- Keep the distinction explicit: Explore proves only that each adjacent scheduled edge was observed independently. It does not prove date compatibility, connection protection, fare availability, visa eligibility, or a through-ticket.
- Reuse `GET /api/v1/explore/routes` for every onward airport. Weekly caching, miss coalescing, stale fallback, defensive pagination, and the shared FlightAPI gate apply unchanged.

## Implemented verification

- [x] Backend tests cover v1 response parsing, multi-page aggregation, codeshare deduplication, coordinate filtering, invalid origins, empty schedules, later-page failure, cache profiles, stale fallback, cache diagnostics, transient retry, safe `502` responses, and request coalescing.
- [x] Mixed concurrency tests combine schedule pages, airport autocomplete, simple fare searches, and multi-destination calls and assert that live FlightAPI concurrency never exceeds the configured shared limit.
- [x] API contract tests cover Explore and homepage responses, including complete and stale metadata.
- [x] Frontend tests cover airport selection, globe loading, errors, empty results, destination highlighting, explicit Search action, `Surprise me`, and prefilled Search not auto-starting.
- [x] Onward-exploration tests cover hover/focus arc isolation, committed-leg preservation, candidate replacement and clearing, breadcrumb truncation, browser Back restoration, repeated-airport handling, maximum-leg enforcement, and stale/error states after changing the active origin.
- [x] Multi-destination hydration tests cover adjacent-leg generation, default dates, ordered-mode selection, edit/submit prefill removal, and no automatic provider request.
- [x] Homepage and globe tests cover the random-hub preview, Explore call to action, lazy loading, animation timing, hover camera movement, reduced motion, stable responsive sizing, cleanup, and WebGL fallback.
- [x] Accessibility tests cover keyboard destination selection, the screen-reader destination list, progress announcements, and non-canvas fallback content.
- [x] The production frontend build and complete automated backend and frontend suites pass.

## Assumptions and deferred work

- Phase one shows the current direct network observed in FlightAPI v1's rolling schedule window.
- Broader seasonal sampling through v2 is deferred to phase two because it costs more credits, remains incomplete, and may overcrowd the visualization.
- A scheduled route does not imply current fare availability; Search remains authoritative.
- Destination selection never redirects immediately. `Surprise me` and manual selection share the same explicit Search, onward-exploration, and Multi-destination actions.
- Repeating an airport already present in the committed path is rejected to prevent accidental cycles in phase one. Returning to the starting airport remains available later through Build my route's existing `Return to starting point` control.
- The homepage globe is a preview and does not redirect when a destination is clicked.
