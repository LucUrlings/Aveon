# Multi-destination rollout and rollback

Multi-destination search is controlled independently from simple search by `MultiDestinationSearch:Enabled`. Docker Compose maps this to `MULTI_DESTINATION_SEARCH_ENABLED`, which defaults to `true`. Disabling it makes every `/api/v1/itinerary-searches` endpoint return `404` while the existing one-way and return endpoints continue operating normally; the frontend presents this as an unavailable-feature message.

## Before changing deployment state

1. Deploy exactly one backend instance. The FlightAPI request gate is process-local and horizontal scaling is prohibited until a distributed gate is implemented.
2. Run the backend and frontend suites and a production frontend build.
3. Confirm `FLIGHTAPI_MAX_CONCURRENT_REQUESTS` matches the provider subscription. It defaults to five and all simple-search, autocomplete, ordered-route, and optimized-route live requests share it.
4. Confirm the role budgets and execution timeout. No role budget may exceed `MultiDestinationSearch:HardMaxProviderCalls`.
5. Verify logs and analytics contain no API key, booking URL, booking token, airport selection, or search identifier.

## Staged rollout

The application feature flag is global. Audience staging must therefore be enforced at the deployment or access-control layer; do not describe the current flag as an administrator-only switch.

1. Set `MULTI_DESTINATION_SEARCH_ENABLED=true` in a restricted staging deployment.
2. Exercise ordered and optimized searches at anonymous, registered-user, and administrator budgets.
3. Monitor `itinerary_search.*` and `flightapi.*` metrics for failures, bounded coverage, execution duration, retained results, active searches, queued provider calls, cache hits, and `429` responses.
4. Enable the public production deployment only after provider concurrency, memory, and latency remain within their configured bounds.

## Rollback

1. Set `MULTI_DESTINATION_SEARCH_ENABLED=false` and redeploy or restart the single backend instance.
2. Verify `/api/v1/itinerary-searches/configuration` returns `404` and the simple `/api/v1/searches` flow still passes its smoke check.
3. Allow already-running worker calls to end within their configured timeout. Redis sessions expire automatically and no user record depends on them.
4. Preserve aggregate metrics and sanitized logs for diagnosis; never export provider credentials or booking URLs.

Rollback does not require a database migration, Redis deletion, or frontend rollback.
