# Multi-Destination Travel Search Product Plan

Status: Milestones 0–4 and 6 complete; Milestone 5 provider-bundled fares deferred
Last updated: 2026-08-02
Owner: Aveon

This document is the source of truth for Aveon's expansion from flexible flight comparison into multi-destination travel search. Every implementation task and pull request for this initiative must reference a milestone and acceptance criterion from this plan. If a product decision changes, update this document before changing the implementation.

## 1. Goal and exact end result

Aveon will let a traveller describe a multi-destination trip in terms of places, acceptable airports, dates, stay requirements, endpoint requirements, and optimization priorities. Aveon will progressively find, price, rank, and explain complete bookable itineraries.

The finished product will provide three search experiences:

1. **Simple search** keeps the existing one-way and return flow.
2. **Build my route** accepts an ordered list of exact flight legs. Every endpoint may contain multiple acceptable airports.
3. **Optimize my trip** accepts destination groups and determines the travel dates, airports, and flights that best satisfy the user's constraints. By default it preserves the order shown; the traveller can allow it to compare alternate destination orders.

The multi-destination result page shows **separate-ticket itineraries** assembled by Aveon from independently bookable one-way fares. The result contract keeps a booking-type field so a future provider can add other booking models without changing the public shape.

Every complete itinerary will show its total price, flight duration, calendar schedule, stops, airport choices, booking count, booking links, ranking explanation, and relevant risk warnings.

The core product promise is:

> Tell Aveon where you are willing to go and the rules your trip must follow. Aveon will search the viable routes and show the best complete journeys it found, including options a conventional ordered search would never request.

## 2. Locked product decisions

These decisions are approved and must not be reopened during implementation unless this plan is explicitly revised.

### 2.1 Destination groups

- A destination group is created by the user.
- It has a user-facing label and one or more IATA airport codes.
- Airports do not need to be in the same city or country.
- Aveon will not automatically expand countries or regions in the first release.
- Example: `Netherlands` may contain `AMS`, `EIN`, `CGN`, and `BRU`.

### 2.2 Optimized-trip endpoints

The user can choose one of three endpoint modes:

- `returnToStart`: visit every destination and finish at the starting airport group.
- `openEnded`: visit every destination and allow Aveon to choose which destination is last.
- `fixedEnd`: visit every unordered destination and finish at a separate user-defined airport group.

A fixed ending group is not also an unordered destination. Repeated visits belong in the ordered route builder, not the first optimizer release.

### 2.3 Dates and stays

- The first release uses an exact trip start date and exact trip end date.
- Total trip length is derived from those dates and is not entered separately.
- The first inter-city leg departs on the start date.
- For `returnToStart` and `fixedEnd`, the final leg's local arrival date must equal the end date.
- For `openEnded`, the stay at the final destination must finish on the end date.
- Every unordered destination has either:
  - `minimumNights`: stay at least the specified number of calendar nights.
  - `exactNights`: stay exactly the specified number of calendar nights.
- Nights are measured using local calendar dates between arrival and the next departure. For an open-ended final destination, nights are measured between arrival and the trip end date.
- At most one inter-city itinerary leg may depart on a calendar day. Connections within a provider itinerary remain part of that single leg.
- Overnight flights may arrive on a later calendar date and reduce the time available for the next stay.
- The UI calculates a preliminary minimum trip length before submission. The backend performs the authoritative feasibility validation using actual flight timestamps.

Flexible outer date windows are intentionally deferred until the fixed-date optimizer is stable.

### 2.4 Airport continuity

- A trip has a default airport-continuity rule.
- Every destination may override that default.
- `sameAirport` requires the next leg to depart from the airport at which the previous leg arrived.
- `allowSwitch` permits arrival at one group airport and departure from another.
- Every actual airport switch is shown prominently in the itinerary.
- Ground-transfer time, price, feasibility, visa requirements, and baggage handling are not included in the first release.
- Results with an airport switch display a warning before any booking link.

### 2.5 Ranking

Every result set offers:

- `recommended`: balances price, flight duration, stops, separate bookings, and airport switches.
- `cheapest`: sorts complete discovered itineraries by total bookable price, then duration.
- `fastest`: sorts by summed provider itinerary duration, then price.

Fastest does not include unmodelled ground-transfer time. The UI states this whenever an itinerary changes airports.

Recommended uses the existing price-versus-time philosophy, extended for multi-leg risks:

```text
timeValuePerHour = clamp(lowestCompletePrice * 0.10, 10 EUR, 30 EUR)

recommendedScore =
  totalPrice
  + additionalFlightHoursComparedWithFastest * timeValuePerHour
  + totalStops * timeValuePerHour * 0.75
  + additionalBookings * timeValuePerHour * 1.50
  + airportSwitches * timeValuePerHour
```

The result includes the score inputs so the frontend can explain why an itinerary is recommended. The ranking choice is not saved to the browser or user account and always defaults to recommended.

### 2.6 Search truthfulness

- `cheapest` means the lowest-priced complete itinerary discovered during that search.
- Aveon must not claim a mathematical global minimum when the provider-call or state budget prevented exhaustive evaluation.
- The result page reports whether coverage was `exhaustive` or `bounded`.
- A bounded search reports provider calls used, candidate states evaluated, and the configured limit.
- Cached provider responses count as evaluated edges but do not consume a live provider-call budget.

## 3. User experience

### 3.1 Search entry

The application exposes dedicated entry points:

- `/search` contains `One way` and `Return`.
- `/explore` maps exact-date direct destinations for the first leg, offers rolling onward route suggestions, and can hand a selected route plus its first leave date to Search or Build my route.
- `/multi-destination` contains `Build my route` and `Optimize my trip`; Build my route is the default tab.

The existing one-way and return experiences remain operational throughout development.

### 3.2 Optimize my trip form

The form contains:

1. Starting destination group and its accepted airports.
2. Endpoint mode and, when required, a fixed ending group.
3. Exact trip start and end dates.
4. A form list of destination cards with “Keep destinations in the order shown” enabled by default; disabling it lets the optimizer compare alternate route orders.
5. A stay mode and night count for every destination.
6. Trip-wide airport-continuity default and per-destination overrides under advanced controls.
7. Passengers and cabin class.
8. Initial ranking preference for the result view, defaulting to recommended without persistence.

Before submission, the form displays:

- Number of required inter-city legs.
- Minimum feasible calendar length based on stay rules.
- Estimated search size.
- Whether the search is expected to be exhaustive or bounded.
- A clear validation error if the date range is impossible.

### 3.3 Build my route form

- The user adds ordered leg rows.
- Every leg has a `from` airport group, `to` airport group, and exact departure date.
- Each airport group accepts multiple airports.
- Consecutive legs may enforce the same-airport rule or allow an airport switch.
- The user may finish anywhere.
- An optional `Return to starting point` control appends a final dated leg back to the first airport group.
- Version one supports up to eight separately priced legs.

### 3.4 Progressive result page

The result page renders while the session is running and shows phases:

1. Validating trip constraints.
2. Searching flight edges.
3. Building complete itineraries.
4. Finalizing rankings.

Only complete itineraries appear in the main results. Partial paths may be summarized in progress information but are never presented as bookable results.

Each itinerary card contains:

- Full destination timeline and local dates.
- Flight details for every leg.
- Stay nights between legs.
- Total fare and per-leg fare.
- Total flight duration and total stops.
- Accepted-airport choices actually used.
- `Separate tickets` badge.
- Number of transactions required.
- Airport-change and separate-ticket warnings.
- Expandable score explanation.
- One booking link per separate fare.

The page supports recommended, cheapest, and fastest tabs with the leading price and duration visible for immediate comparison.

Multi-destination results expose the same core refinement vocabulary as simple search, applied to complete multi-leg itineraries:

- Stop selections require every itinerary leg to fall within one of the enabled stop buckets.
- Maximum duration uses total in-air itinerary duration.
- Departure time and airport apply to the first leg; arrival time and airport apply to the final leg.
- Airline selections require every flight segment to use a selected airline.
- Booking-source selections require every booking in an itinerary to use a selected source.
- Maximum booking count and whether airport switches are allowed are additional multi-destination-only filters.

Filtering never changes the routes evaluated by the optimizer. It refines the retained complete itineraries and therefore does not change the reported search coverage.

## 4. Public contracts

Multi-destination travel search uses a separate API and domain model. The current `SearchRequest` and `/api/v1/search` contract remain unchanged.

### 4.1 Request types

```text
AirportGroupRequest
  id: string
  label: string
  airportCodes: string[]

StayRuleRequest
  mode: minimumNights | exactNights
  nights: integer

DestinationRequest
  group: AirportGroupRequest
  stay: StayRuleRequest
  airportContinuity: inherit | sameAirport | allowSwitch

OptimizedTripRequest
  mode: optimize
  start: AirportGroupRequest
  destinations: DestinationRequest[]
  endpointMode: returnToStart | openEnded | fixedEnd
  fixedEnd: AirportGroupRequest | null
  startDate: DateOnly
  endDate: DateOnly
  defaultAirportContinuity: sameAirport | allowSwitch
  adults: integer
  cabinClass: string
  ranking: recommended | cheapest | fastest

OrderedLegRequest
  id: string
  from: AirportGroupRequest
  to: AirportGroupRequest
  departureDate: DateOnly
  airportContinuityWithPrevious: sameAirport | allowSwitch

OrderedTripRequest
  mode: ordered
  legs: OrderedLegRequest[]
  adults: integer
  cabinClass: string
  ranking: recommended | cheapest | fastest
```

IDs supplied by the client are correlation identifiers only. The backend trims labels, uppercases and deduplicates IATA codes, and performs all authoritative validation.

### 4.2 Endpoints

```text
POST   /api/v1/itinerary-searches
GET    /api/v1/itinerary-searches/configuration
GET    /api/v1/itinerary-searches/{searchId}
DELETE /api/v1/itinerary-searches/{searchId}
```

- `POST` accepts either optimized or ordered input and returns a running session immediately.
- `GET configuration` returns the authenticated caller's provider-call allowance and current multi-destination input limits for accurate preliminary UI feedback.
- `GET` accepts page, page size, ranking, and result filters. It returns progress, coverage, warnings, and complete itineraries.
- `DELETE` requests cancellation, stops scheduling new provider calls, and marks the session canceled. Completed provider calls may still populate the shared provider cache.
- Search sessions remain in Redis and use the configured search-session TTL.
- The API returns `400` for invalid, infeasible, or over-limit input and `404` for an expired session. Validation responses identify the exact field or configured limit that must change.

### 4.3 Session and result types

```text
ItinerarySearchSession
  searchId
  mode
  status: running | completed | partial | failed | canceled
  phase
  progress
  coverage
  results
  warnings
  errorMessage

SearchCoverage
  mode: exhaustive | bounded
  liveProviderCallsUsed
  providerCallLimit
  cacheHits
  candidateStatesEvaluated
  candidateStatesPruned

ItineraryResult
  id
  bookingType: separateTickets in the current provider implementation; extensible for future providers
  destinationOrder
  legs
  stays
  totalPrice
  totalFlightDurationMinutes
  totalStops
  bookingCount
  airportSwitches
  bookingOptions
  warnings
  rankingBreakdown
```

Result pagination defaults to 25 complete itineraries and is capped at 100 per response. The session retains at most 100 complete itineraries in total, selected from the union of the best candidates for each ranking mode.

## 5. Search engine design

### 5.1 Separation of responsibilities

Create a new `ItinerarySearch` backend feature with these responsibilities:

- Request validation and normalization.
- Feasible schedule generation.
- Provider edge acquisition.
- Itinerary optimization.
- Progressive session snapshots.
- Filtering, ranking, pagination, and cancellation.

The provider layer exposes one-way edge searches used by both ordered and optimized routes:

```text
SearchOneWayEdgeAsync(request)
```

### 5.2 One-way edge identity and cache

A price edge is identified by:

```text
provider
origin airport
destination airport
departure date
passenger composition
cabin class
currency
documented locale inputs that affect price, when the provider request actually supports them
```

The existing Redis provider cache is extended to store these normalized edge responses. Cache keys never include secrets. Concurrent requests for the same missing edge are coalesced so only one live provider call is made.

### 5.3 Feasibility engine

Before pricing, the feasibility engine:

- Normalizes airport groups and rejects empty groups.
- Rejects duplicate destination IDs and duplicate airport codes within a group.
- Rejects a fixed ending group that duplicates an unordered destination.
- Calculates required leg count for the endpoint mode.
- Rejects negative nights and unsupported passenger or cabin values.
- Rejects an end date before the start date.
- Rejects date windows that cannot fit the travel legs and stay rules.
- Generates only schedules that can still reach a valid endpoint by the end date.

Limits for the first optimized release:

- Maximum five unordered destination groups.
- Maximum five airports per group.
- Maximum 31 calendar days from start through end.
- Maximum eight ordered legs.

These are configuration values with the listed defaults. The API includes the violated limit in validation responses.

### 5.4 Optimizer state

The optimizer uses a time-aware state:

```text
visited destination bit set
current destination group
current arrival airport
current local arrival timestamp
next eligible departure date
accumulated price
accumulated flight duration
accumulated stops
booking count
airport switches
selected fare legs
```

For `sameAirport`, outgoing edges must originate at `current arrival airport`. For `allowSwitch`, outgoing edges may originate at any airport in the current group and the state records a switch when the codes differ.

### 5.5 Bounded graph search

- Generate transitions lazily rather than materializing the cartesian product of routes, dates, airports, and fares.
- Use a priority queue ordered by the active ranking.
- Maintain a Pareto frontier for equivalent state keys across price and flight duration.
- Remove dominated states.
- Pareto-prune and rank provider fares immediately after each edge response, retaining only the configured best non-dominated edge candidates before expanding path states.
- Keep at most 25 non-dominated candidates per state key by default.
- Keep at most 10,000 candidate paths in any active frontier by default.
- Continue after frontier pruning until a separate cumulative 500,000-state evaluation budget is reached by default.
- Divide the cumulative evaluation budget into a per-provider-edge allowance derived from the caller's provider-call limit, so one heavily connected edge cannot consume the entire search.
- Explore unseen origin/destination/date edges before repeatedly deepening paths over already loaded edges; saturated edges are skipped and reported as bounded coverage.
- Stop issuing live provider calls at the role-based call budget.
- Continue optimization using already cached edges after the live-call budget is reached.
- Persist a bounded snapshot whenever a complete itinerary is added or progress materially changes.
- Never retain more than the configured result and state limits.

If every feasible transition in the accepted input space was evaluated using all usable fares returned by the provider, coverage is exhaustive. If any candidate was skipped because of a call, state, or time budget, coverage is bounded.

### 5.6 Shared FlightAPI concurrency gate

`FlightApi:MaxConcurrentRequests` models simultaneous live HTTP requests, not a requests-per-second rate. Aveon defaults it to five for the currently configured subscription while allowing any higher positive value when the provider allowance is upgraded.

- Use one singleton provider request gate with `FlightApi:MaxConcurrentRequests`, defaulting to `5` while allowing a higher positive value when the FlightAPI subscription is upgraded.
- Enforce the gate inside the FlightAPI provider boundary immediately before a real HTTP request, not independently in each search service.
- Apply the same configured allowance to airport lookup, departure-schedule pages, one-way, round-trip, ordered-route, and optimized-route calls combined.
- Acquire exactly one permit for each live HTTP request and release it after the response has been consumed or failed.
- Perform cache lookup and identical-request coalescing before acquiring a permit. Cache hits do not consume provider concurrency.
- Per-search worker pools and provider-call budgets may schedule work, but they never create additional provider concurrency allowances.
- Remove service-level acquisition around calls that are already protected at the provider boundary to prevent double acquisition and deadlock.
- Treat provider `429` responses as throttling signals, honor `Retry-After` when present, and use bounded retry with jitter within the session execution timeout.
- Emit metrics for current permits, queued requests, cache hits, live calls, and `429` responses without logging API keys or booking URLs.

The first release runs one backend application instance, so the singleton gate is process-wide and deployment-wide. Horizontal scaling is not allowed until the gate is replaced by a Redis-backed distributed lease or the configured allowance is divided safely across instances.

### 5.7 Per-search provider budgets

Multi-destination searches have dedicated configurable limits instead of reusing simple-search combination counts:

```text
MultiDestinationSearch:AnonymousMaxProviderCalls = 25
MultiDestinationSearch:UserMaxProviderCalls = 100
MultiDestinationSearch:AdminMaxProviderCalls = 250
MultiDestinationSearch:HardMaxProviderCalls = 500
MultiDestinationSearch:MaxActiveStates = 10000
MultiDestinationSearch:MaxEvaluatedStates = 500000
MultiDestinationSearch:MaxCandidatesPerState = 25
MultiDestinationSearch:MaxStoredResults = 100
MultiDestinationSearch:ExecutionTimeoutMinutes = 10
```

All live provider calls still pass through the shared FlightAPI concurrency gate. Per-search budgets control total work, while the shared gate controls combined concurrent pressure across every active simple and multi-destination search. No role can exceed the hard provider-call, concurrency, memory, result, or execution-time safety limits.

## 6. Provider scope

Aveon's optimizer remains the source of truth and currently assembles complete routes from independently priced one-way FlightAPI responses. Provider-bundled itinerary APIs are outside the implemented scope. The common result contract remains extensible so a future provider can introduce another booking type without coupling the optimizer to FlightAPI-specific concepts.

## 7. Delivery milestones

Each milestone must finish with its tests and acceptance gate before the next milestone begins.

### Milestone 0: Contracts, shared controls, and provider gate

- [x] Add the multi-destination-search options and validated configuration.
- [x] Add request, session, coverage, result, warning, and ranking contracts.
- [x] Add the separate controller routes with feature flag protection.
- [x] Add generated frontend API types.
- [x] Add empty Redis session lifecycle with cancellation.
- [x] Extract the existing accessible airport chips, input, autocomplete, keyboard navigation, and suggestion list into a reusable `AirportGroupPicker`.
- [x] Adopt `AirportGroupPicker` in the existing simple search and multi-destination form scaffolds.
- [x] Move authoritative provider concurrency acquisition to the FlightAPI provider boundary, after cache lookup and immediately before every real HTTP request.
- [x] Route every FlightAPI operation—airport lookup/autocomplete, departure schedules, one-way, and round-trip—through one singleton FlightAPI gate.
- [x] Remove service-level provider permit acquisition and local provider-limit semaphores so one HTTP request never acquires two permits.
- [x] Add bounded `429` retry with jitter, provider-gate metrics, and the single-instance deployment constraint.

Acceptance gate:

- The current search API is unchanged.
- Valid optimized and ordered requests create a running session.
- Invalid discriminators and structurally invalid requests return actionable validation errors.
- Canceling a session produces a stable canceled state.
- Simple search and the multi-destination form scaffolds render the same `AirportGroupPicker` behavior for selecting, deduplicating, removing, and keyboard-selecting airports.
- A mixed test workload containing airport lookup, departure schedules, one-way, and round-trip operations never exceeds five combined live requests with the default configuration.
- Airport autocomplete cannot bypass the provider limit.
- Cache hits consume no permits.
- Every successful, failed, canceled, and timed-out request releases its permit.
- No code path acquires two permits for one HTTP request.
- `429` responses honor `Retry-After` when present and retry only within the bounded session execution timeout.
- Metrics report current permits, queued requests, cache hits, live calls, and `429` responses without API keys or booking URLs.
- The application is documented and deployed as one backend instance; horizontal scaling remains prohibited until a distributed gate is in place.

### Milestone 1: Ordered route with separate tickets

- [x] Build the ordered-route form with reusable airport-group controls.
- [x] Use `AirportGroupPicker` for every ordered-leg endpoint, matching simple-search airport-group behavior.
- [x] Route every ordered-search provider request through the shared FlightAPI gate.
- [x] Search all accepted airport pairs for each exact leg within the provider budget.
- [x] Combine legs using continuity-aware dynamic programming.
- [x] Return progressive complete separate-ticket itineraries.
- [x] Add ordered-route ranking, pagination, warnings, and booking links.
- [x] Establish the shared multi-destination result filter model and UI for ordered results, ready for optimized results to reuse.

Acceptance gate:

- A three-leg route with multiple airports per endpoint produces complete bookable itineraries.
- Same-airport continuity excludes invalid airport changes.
- Allowed airport changes are retained and warned about.
- Cheapest and fastest select the correct itinerary from deterministic provider fixtures.
- Ordered results support the shared complete-itinerary filters and advanced booking-risk filters.
- The UI works with keyboard navigation and at mobile width.
- Concurrent simple and ordered searches share the same FlightAPI permits (five by default).

### Milestone 2: Optimizer feasibility and scheduling

- [x] Implement endpoint modes and stay rules.
- [x] Implement preliminary frontend feasibility feedback.
- [x] Implement authoritative backend schedule generation and pruning.
- [x] Add endpoint, night, overnight-arrival, and one-leg-per-day tests.

Acceptance gate:

- Return-to-start, open-ended, and fixed-end requests produce only valid abstract schedules.
- Minimum stays may absorb spare nights.
- Exact stays never absorb additional nights.
- Impossible date windows fail before provider calls begin.

### Milestone 3: Priced unordered optimization

- [x] Implement the bounded time-aware graph search.
- [x] Integrate edge caching, request coalescing, global concurrency, and role budgets.
- [x] Persist progressive complete itineraries and coverage metrics.
- [x] Implement cancellation and execution timeout throughout the worker pipeline.

Acceptance gate:

- Deterministic graph fixtures return the correct cheapest, fastest, and recommended routes.
- Open-ended mode chooses the correct final destination.
- Fixed-end and return-to-start modes terminate only at the required endpoint.
- No search exceeds configured call, state, result, concurrency, or time limits.
- Partial provider failure yields partial complete results and a `partial` status rather than discarding successful work.
- Concurrent simple, ordered, and optimized searches share the same FlightAPI permits (five by default).

### Milestone 4: Optimizer frontend

- [x] Add the Multi-destination entry point and dedicated route.
- [x] Add destination cards, stay controls, endpoint controls, and airport-continuity overrides.
- [x] Use `AirportGroupPicker` for every optimizer airport group, matching simple-search and ordered-route behavior.
- [x] Add progressive phase and coverage UI.
- [x] Add itinerary timeline cards and ranking comparison tabs.
- [x] Reuse the ordered-result filter model and UI for optimized results, applying stop, airline, and booking-source selections across every itinerary leg.
- [x] Add multi-destination-only maximum booking count and airport-switch filters, plus infinite pagination, retry, cancellation, and empty states.

Acceptance gate:

- A user can configure every locked product decision without editing raw data.
- All warnings appear before booking actions and are announced accessibly.
- A second search cancels and can no longer be overwritten by the first.
- Reloading a running session resumes polling when the session still exists.
- Desktop and mobile layouts pass the established accessibility and responsive checks.

### Milestone 5: Provider-bundled multi-trip fares — deferred

The original milestone investigated FlightAPI Multi Trip results as an alternative to Aveon's separate-ticket assembly. Live 3-, 4-, and 5-leg probes on 2026-08-01 were rejected because the configured account does not include the required provider subscription. The experimental runtime path and configuration were removed rather than retaining unreachable code.

Current behavior:

- Ordered and optimized results are assembled from independently bookable one-way fares.
- `bookingType` remains part of the public result contract so another provider or future supported subscription can add a different booking model without redesigning the frontend contract.
- No `FLIGHTAPI_MARKET`, Multi Trip call budget, or dead Multi Trip client code remains.
- This milestone must be replanned and tested against an available provider contract before implementation resumes.

### Milestone 6: Hardening and release

- [x] Add structured metrics and logs without secrets or booking tokens.
- [x] Add load tests at all configured input limits.
- [x] Add feature-flag rollout and rollback documentation.
- [x] Update README, How Search Works, About, metadata, and user-facing terminology.
- [x] Add analytics events for form abandonment, validation failure, completed search, bounded coverage, result selection, and booking click.

Acceptance gate:

- Existing one-way and return regression suites pass.
- Multi-destination backend and frontend suites pass.
- Maximum-size searches remain within configured bounded collections.
- Provider credentials and booking tokens are absent from logs.
- The feature can be disabled without affecting simple search.

### Post-milestone integration completed on 2026-08-02

- Explore can prefill a one-leg normal search without automatically starting provider calls.
- Explore can hand an airport chain to Build my route with its exact first leave date. Later dates remain empty, an availability warning explains that onward suggestions may not operate or return fares, and the form waits for explicit submission.
- Normal search automatically falls back to the exact fewest available stop count when the default direct-only result set is empty; filter-aware stop counts remain visible and manual stop changes remove the automatic exact-stop constraint.
- Return-search outbound cards and summaries explicitly identify prices that exclude the inbound fare and place the outbound-selection action next to the price.
- FlightAPI departure-schedule calls use the same provider gate, retry policy, diagnostics, and single-instance constraint as all fare and autocomplete calls.
- The separate Explore implementation contract lives in [`explore-routes-plan.md`](explore-routes-plan.md).

## 8. Test strategy

### Backend unit tests

- Request normalization and validation.
- Minimum and exact night calculations.
- All endpoint modes.
- Same-airport and airport-switch transitions.
- Overnight and timezone-aware arrival handling.
- Dominance and Pareto-frontier pruning.
- Cheapest, fastest, and recommended scoring.
- Exhaustive versus bounded coverage classification.
- Provider-call, state, result, and timeout limits.
- Cache-key normalization and concurrent edge coalescing.
- Mixed-operation enforcement of the single shared provider concurrency gate.
- Permit release after success, failure, cancellation, timeout, and `429` retry.
- Mixed cache-miss traffic from simple search, multi-destination search, Explore schedule pages, and airport autocomplete never exceeds the configured concurrent live FlightAPI request limit; with the default configuration, assert a maximum of five.
- Cancellation and partial provider failure.

### Backend integration tests

- Full Redis-backed session lifecycle.
- Progressive session snapshots.
- Deterministic route graphs with known optimal answers.
- Pagination and ranking over complete itineraries.
- Authentication-based provider-call limits.
- Feature-flag behavior.

### Frontend tests

- Airport-group creation and deduplication.
- Dynamic destination and ordered-leg controls.
- Endpoint-dependent form changes.
- Minimum trip-length feedback.
- Multi-destination control inheritance and overrides.
- Request serialization and session hydration.
- Polling cancellation and stale-response protection.
- Progressive phases and bounded-coverage explanation.
- Booking and airport-switch warnings.
- Ranking, filtering, and pagination.
- Shared `AirportGroupPicker` behavior in simple search, ordered routes, and optimized trips.
- Shared filter-contract and component behavior for ordered and optimized results, including all-leg matching, maximum booking count, and airport-switch controls.
- Keyboard, screen-reader, reduced-motion, timezone, and mobile behavior.

### Performance tests

- Maximum destinations, airports, and date span.
- High cache-hit and high cache-miss scenarios.
- Concurrent users sharing provider edges.
- Slow and failing provider responses.
- Assertions that active states, retained results, live calls, and execution time never exceed configuration.

## 9. Release strategy

The initial implementation was shipped behind the feature flag and exercised through the staged process below. The current default is `MultiDestinationSearch:Enabled=true`.

1. Keep ordered and optimized search independently controlled by `MultiDestinationSearch:Enabled`.
2. Before changing its deployment state, run deterministic and maximum-input tests and follow the operational checklist.
3. Exercise anonymous, registered-user, and administrator budgets while monitoring bounded coverage, failures, duration, retained results, and provider pressure.
4. Disable the flag for immediate rollback without changing simple Search or Explore.

The application flag is global; audience-specific staging requires deployment-layer access control. Rollback consists of disabling the flag. No existing simple-search contract or persisted user record depends on multi-destination search. The operational procedure is documented in [`multi-destination-rollout.md`](multi-destination-rollout.md).

## 10. Explicitly deferred work

The following are not required for the first completed version:

- Automatically generated country, region, or city airport groups.
- Ground-transfer duration, price, or booking.
- Trains, buses, ferries, hotels, or activities.
- Flexible outer start and end date windows.
- More than five unordered destinations or a trip longer than 31 days.
- Repeated visits to the same destination in optimized mode.
- Saved trips, collaboration, notifications, or price tracking.
- Children and infant passenger inputs beyond the current supported passenger model.
- A guarantee of the global cheapest route for a bounded search.
- FlightAPI Multi Trip bundled fares. Live 3-, 4-, and 5-leg checks on 2026-08-01 were rejected because the configured account lacks the required STANDARD or PLUS subscription. No Multi Trip runtime code or configuration is retained; reconsider only if a supported subscription or another bundled-fare provider becomes available.

## 11. Final definition of done

The multi-destination search initiative is complete when all of the following are true:

- The existing simple one-way and return experience still works.
- A traveller can construct and search an ordered route with multiple airports at every endpoint.
- Simple search, ordered routes, and optimized trips use the same accessible airport-group selection control.
- A traveller can preserve the destination order shown or allow reordering, with exact dates, minimum or exact stays, all three endpoint modes, and configurable airport continuity.
- Aveon progressively returns complete ranked itineraries without materializing an unbounded combination set.
- Every itinerary is transparent about bookings, airport switches, unmodelled transfers, coverage, and risk.
- Search work is bounded by configured provider-call, memory, result, concurrency, and execution-time limits.
- All simple-search, multi-destination, and airport-lookup/autocomplete traffic shares one authoritative configurable FlightAPI concurrency gate at the provider boundary (five permits by default); cache hits consume no permits and no request can acquire two.
- Cancellation, partial failure, stale requests, pagination, mobile layout, accessibility, and timezone behavior are covered by automated tests.
- Production rollout and rollback are controlled by a feature flag independent from simple search.
- User-facing documentation accurately explains what Aveon searches, what it optimizes, and what it cannot guarantee.
