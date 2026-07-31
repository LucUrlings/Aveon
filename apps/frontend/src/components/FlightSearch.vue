<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import FlightSearchBar from './flight-search/FlightSearchBar.vue'
import SearchFilters from './flight-search/SearchFilters.vue'
import SearchProgress from './flight-search/SearchProgress.vue'
import SearchResultsPanel from './flight-search/SearchResultsPanel.vue'
import { rankReturnOptions, returnRankingOptions, type ReturnRanking } from '../features/flight-search/returnRanking'
import { useAirportPicker } from '../features/flight-search/useAirportPicker'
import { useSearchDates } from '../features/flight-search/useSearchDates'
import { useSearchFilters } from '../features/flight-search/useSearchFilters'
import { useSearchRouteState } from '../features/flight-search/useSearchRouteState'
import { useSearchSession } from '../features/flight-search/useSearchSession'
import { buildSearchRequestKey, getExplicitSelection } from '../features/flight-search/searchRoute'
import {
  cabinOptions,
  type AirportOption,
  type SearchRequest,
  type SearchResult,
  type SearchResultsQuery,
} from '../features/flight-search/types'

const MAX_DEPARTURE_RANGE_DAYS = 10
const DEFAULT_PAGE_SIZE = 100

const originPicker = useAirportPicker([
  { code: 'DUB', name: 'Dublin', displayLabel: 'Dublin (DUB)' },
])
const destinationPicker = useAirportPicker([
  { code: 'AMS', name: 'Amsterdam Schiphol', displayLabel: 'Amsterdam Schiphol (AMS)' },
])
const originInput = originPicker.input
const destinationInput = destinationPicker.input
const originAirports = originPicker.airports
const destinationAirports = destinationPicker.airports

const adults = ref(1)
const cabinClass = ref('economy')

const originSuggestions = originPicker.suggestions
const destinationSuggestions = destinationPicker.suggestions

const expandedResultIds = ref<string[]>([])
const isSearchCollapsed = ref(false)

const {
  includeDirectFlights,
  includeOneStopFlights,
  includeTwoPlusStopFlights,
  selectedProviders,
  selectedAirlines,
  selectedDepartureAirports,
  selectedArrivalAirports,
  maxDurationMinutes,
  departureTimeRange,
  arrivalTimeRange,
  returnDepartureTimeRange,
  returnArrivalTimeRange,
} = useSearchFilters()
const selectedOutboundLegId = ref<string | null>(null)
const selectedOutboundResult = ref<SearchResult | null>(null)
const selectedReturnLegId = ref<string | null>(null)
const returnRanking = ref<ReturnRanking>('best')
const searchDates = useSearchDates(() => {
  returnDepartureTimeRange.value = [0, 1439]
  returnArrivalTimeRange.value = [0, 1439]
  selectedOutboundLegId.value = null
  selectedOutboundResult.value = null
  selectedReturnLegId.value = null
  returnRanking.value = 'best'
})
const {
  tripType,
  departureDateFrom,
  departureDateTo,
  selectedDepartureDates,
  returnDateFrom,
  returnDateTo,
  selectedReturnDates,
} = searchDates
let hasMounted = false

const sessionState = useSearchSession({
  buildQuery: () => buildSearchResultsQuery(),
  buildRequest: (): SearchRequest => ({
    originAirports: originAirports.value.map((airport) => airport.code),
    destinationAirports: destinationAirports.value.map((airport) => airport.code),
    departureDates: [...selectedDepartureDates.value],
    returnDates: tripType.value === 'return' ? [...selectedReturnDates.value] : [],
    adults: adults.value,
    cabinClass: cabinClass.value,
  }),
  getSearchKey: () => getCurrentSearchRequestKey(),
  validateRequest: () => tripType.value === 'return' && !selectedDepartureDates.value.some((departureDate) =>
    selectedReturnDates.value.some((returnDate) => returnDate >= departureDate))
    ? 'Select at least one return date on or after a departure date.'
    : null,
  isReady: () => hasMounted,
  onSearchReset: () => {
    expandedResultIds.value = []
    selectedOutboundLegId.value = null
    selectedOutboundResult.value = null
    selectedReturnLegId.value = null
    returnRanking.value = 'best'
  },
  onSearchAccepted: () => {
    isSearchCollapsed.value = true
  },
})
const {
  loading,
  error,
  response,
  searchSession,
  loadedResults,
  currentPage,
  isLoadingMore,
  isPolling,
  hasMoreResults,
  lastExecutedSearchKey,
  search: searchFlights,
  scheduleRefresh: scheduleSearchSessionRefresh,
  loadNextPage,
  dispose: disposeSearchSession,
} = sessionState

const getCurrentSearchRequestKey = () => {
  const origins = uniqueAirportCodes(originAirports.value)
  const destinations = uniqueAirportCodes(destinationAirports.value)
  const dates = [...selectedDepartureDates.value].sort((left, right) => left.localeCompare(right))

  if (origins.length === 0 || destinations.length === 0 || dates.length === 0) {
    return null
  }

  return buildSearchRequestKey(
    origins,
    destinations,
    dates,
    tripType.value,
    tripType.value === 'return' ? selectedReturnDates.value : [],
    adults.value,
    cabinClass.value,
  )
}

const providerFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return response.value.filters.providers.map((option: { value: string }) => option.value)
})

const airlineFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return response.value.filters.airlines.map((option: { value: string }) => option.value)
})

const departureAirportFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return response.value.filters.departureAirports.map((option: { value: string }) => option.value)
})

const arrivalAirportFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return response.value.filters.arrivalAirports.map((option: { value: string }) => option.value)
})

const availableMaxDurationMinutes = computed(() => {
  if (!response.value) {
    return 0
  }

  return response.value.filters.durationMinutes.max
})

const { hasHydratedFiltersFromUrl, initialize: initializeRouteState } = useSearchRouteState({
  originAirports,
  destinationAirports,
  selectedDepartureDates,
  departureDateFrom,
  departureDateTo,
  tripType,
  selectedReturnDates,
  returnDateFrom,
  returnDateTo,
  adults,
  cabinClass,
  includeDirectFlights,
  includeOneStopFlights,
  includeTwoPlusStopFlights,
  selectedProviders,
  selectedAirlines,
  selectedDepartureAirports,
  selectedArrivalAirports,
  maxDurationMinutes,
  departureTimeRange,
  arrivalTimeRange,
  returnDepartureTimeRange,
  returnArrivalTimeRange,
  selectedOutboundLegId,
  selectedReturnLegId,
  response,
  providerFilters,
  airlineFilters,
  departureAirportFilters,
  arrivalAirportFilters,
  availableMaxDurationMinutes,
  lastExecutedSearchKey,
  search: searchFlights,
  onReady: () => {
    hasMounted = true
  },
})

const filteredResults = computed(() => selectedOutboundLegId.value
  ? rankReturnOptions(loadedResults.value, returnRanking.value)
  : loadedResults.value)
const returnRankingLabel = computed(() => returnRankingOptions.find(
  (option) => option.value === returnRanking.value,
)?.label ?? 'Recommended')
const selectedOutboundSummaryResult = computed<SearchResult | null>(() => {
  if (!selectedOutboundLegId.value) {
    return null
  }

  if (selectedOutboundResult.value?.legs[0]?.id === selectedOutboundLegId.value) {
    return selectedOutboundResult.value
  }

  const matchingResult = filteredResults.value.find(
    (result) => result.legs[0]?.id === selectedOutboundLegId.value,
  )
  const outboundLeg = matchingResult?.legs[0]
  if (!matchingResult || !outboundLeg) {
    return null
  }

  return {
    ...matchingResult,
    isRoundTrip: false,
    legs: [outboundLeg],
    totalDurationMinutes: outboundLeg.durationMinutes,
    priceOptions: [],
  }
})
const paginationSummary = computed(() => {
  if (!response.value || response.value.pagination.totalResults === 0 || filteredResults.value.length === 0) {
    return 'No results'
  }

  const totalResults = response.value.pagination.totalResults
  const end = filteredResults.value.length

  return `Showing ${end} of ${totalResults}`
})

const loadedStopCounts = computed(() => {
  const counts = {
    direct: 0,
    oneStop: 0,
    twoPlusStop: 0,
  }

  for (const result of filteredResults.value) {
    const stops = result.legs.reduce((sum, leg) => sum + Math.max(leg.segments.length - 1, 0), 0)

    if (stops <= 0) {
      counts.direct += 1
    } else if (stops === 1) {
      counts.oneStop += 1
    } else {
      counts.twoPlusStop += 1
    }
  }

  return counts
})

const compactSearchSummary = computed(() => {
  const origins = originAirports.value.map((airport) => airport.code).join(', ')
  const destinations = destinationAirports.value.map((airport) => airport.code).join(', ')
  const dateSummary = selectedDepartureDates.value.join(', ')
  const returnSummary = tripType.value === 'return' && selectedReturnDates.value.length > 0
    ? ` returning ${selectedReturnDates.value.join(', ')}`
    : ''
  return `${origins} to ${destinations} on ${dateSummary}${returnSummary}`
})

const uniqueAirportCodes = (airports: AirportOption[]) =>
  [...new Set(airports.map((airport) => airport.code.trim().toUpperCase()).filter(Boolean))]

const pageTitle = computed(() => {
  const origins = uniqueAirportCodes(originAirports.value).join(', ')
  const destinations = uniqueAirportCodes(destinationAirports.value).join(', ')
  const routeSummary = origins && destinations ? `${origins} to ${destinations}` : ''

  if (isPolling.value && routeSummary) {
    return `Aveon · Searching ${routeSummary}`
  }

  if (response.value && routeSummary) {
    return `Aveon · ${response.value.pagination.totalResults} flights from ${routeSummary}`
  }

  return 'Aveon · Flexible flight search across airports and dates'
})

const searchCombinationCount = computed(() => {
  const origins = uniqueAirportCodes(originAirports.value)
  const destinations = uniqueAirportCodes(destinationAirports.value)
  const departureDates = [...new Set(selectedDepartureDates.value)]
  const departureDateCount = departureDates.length

  if (origins.length === 0 || destinations.length === 0 || departureDateCount <= 0) {
    return 0
  }

  const routeCombinationCount = origins.reduce((count, origin) => (
    count + destinations.filter((destination) => destination !== origin).length
  ), 0)

  if (tripType.value !== 'return') {
    return routeCombinationCount * departureDateCount
  }

  const returnDates = [...new Set(selectedReturnDates.value)]
  const validRoundTripPairCount = departureDates.reduce((count, departureDate) => (
    count + returnDates.filter((returnDate) => returnDate >= departureDate).length
  ), 0)

  return routeCombinationCount * (
    departureDateCount +
    returnDates.length +
    validRoundTripPairCount
  )
})

const arraysEqual = (left: string[], right: string[]) =>
  left.length === right.length && left.every((value, index) => value === right[index])

const syncSelectedFiltersToAvailable = (
  selectedItems: typeof selectedProviders | typeof selectedAirlines | typeof selectedDepartureAirports | typeof selectedArrivalAirports,
  availableItems: string[],
  previousAvailableItems: string[],
  shouldReset: boolean,
) => {
  const hadAllAvailableSelected =
    previousAvailableItems.length === 0
      ? selectedItems.value.length === 0
      : previousAvailableItems.every((item) => selectedItems.value.includes(item))

  if (shouldReset || hadAllAvailableSelected) {
    if (!arraysEqual(selectedItems.value, availableItems)) {
      selectedItems.value = [...availableItems]
    }
    return
  }

  const nextSelectedItems = selectedItems.value.filter((item) => availableItems.includes(item))
  if (!arraysEqual(selectedItems.value, nextSelectedItems)) {
    selectedItems.value = nextSelectedItems
  }
}

const syncMaxDurationToAvailable = (shouldReset: boolean) => {
  if (shouldReset) {
    if (maxDurationMinutes.value !== availableMaxDurationMinutes.value) {
      maxDurationMinutes.value = availableMaxDurationMinutes.value
    }
    return
  }

  const nextMaxDuration = Math.min(maxDurationMinutes.value, availableMaxDurationMinutes.value)
  if (maxDurationMinutes.value !== nextMaxDuration) {
    maxDurationMinutes.value = nextMaxDuration
  }
}

const buildSearchResultsQuery = (): SearchResultsQuery => {
  const query: SearchResultsQuery = {
    direct: includeDirectFlights.value,
    oneStop: includeOneStopFlights.value,
    twoPlusStop: includeTwoPlusStopFlights.value,
    pageSize: DEFAULT_PAGE_SIZE,
  }

  // Provider names and total duration change when independently bookable
  // outbound/inbound fares are combined. Reusing those outbound-stage filters
  // would hide valid recommendations immediately after selecting a leg.
  if (!selectedOutboundLegId.value) {
    const explicitProviders = getExplicitSelection(selectedProviders.value, providerFilters.value)
    if (explicitProviders.length > 0) {
      query.providers = explicitProviders
    }

    if (response.value && maxDurationMinutes.value > 0 && maxDurationMinutes.value < availableMaxDurationMinutes.value) {
      query.maxDuration = maxDurationMinutes.value
    }
  }

  const explicitAirlines = getExplicitSelection(selectedAirlines.value, airlineFilters.value)
  if (explicitAirlines.length > 0) {
    query.airlines = explicitAirlines
  }

  const explicitDepartureAirports = getExplicitSelection(selectedDepartureAirports.value, departureAirportFilters.value)
  if (explicitDepartureAirports.length > 0) {
    query.departureAirports = explicitDepartureAirports
  }

  const explicitArrivalAirports = getExplicitSelection(selectedArrivalAirports.value, arrivalAirportFilters.value)
  if (explicitArrivalAirports.length > 0) {
    query.arrivalAirports = explicitArrivalAirports
  }

  if (departureTimeRange.value[0] !== 0 || departureTimeRange.value[1] !== 1439) {
    query.departureTime = [...departureTimeRange.value] as [number, number]
  }

  if (arrivalTimeRange.value[0] !== 0 || arrivalTimeRange.value[1] !== 1439) {
    query.arrivalTime = [...arrivalTimeRange.value] as [number, number]
  }

  if (tripType.value === 'return') {
    if (returnDepartureTimeRange.value[0] !== 0 || returnDepartureTimeRange.value[1] !== 1439) {
      query.returnDepartureTime = [...returnDepartureTimeRange.value] as [number, number]
    }

    if (returnArrivalTimeRange.value[0] !== 0 || returnArrivalTimeRange.value[1] !== 1439) {
      query.returnArrivalTime = [...returnArrivalTimeRange.value] as [number, number]
    }

    if (selectedOutboundLegId.value) {
      query.outboundLegId = selectedOutboundLegId.value
    }

    if (selectedReturnLegId.value) {
      query.returnLegId = selectedReturnLegId.value
    }
  }

  return query
}

watch(
  response,
  (nextResponse, previousResponse) => {
    const shouldResetFilters = !previousResponse && Boolean(nextResponse) && !hasHydratedFiltersFromUrl.value
    const previousProviderFilters = previousResponse?.filters.providers.map((option: { value: string }) => option.value) ?? []
    const previousAirlineFilters = previousResponse?.filters.airlines.map((option: { value: string }) => option.value) ?? []
    const previousDepartureAirportFilters = previousResponse?.filters.departureAirports.map((option: { value: string }) => option.value) ?? []
    const previousArrivalAirportFilters = previousResponse?.filters.arrivalAirports.map((option: { value: string }) => option.value) ?? []

    syncSelectedFiltersToAvailable(selectedProviders, providerFilters.value, previousProviderFilters, shouldResetFilters)
    syncSelectedFiltersToAvailable(selectedAirlines, airlineFilters.value, previousAirlineFilters, shouldResetFilters)
    syncSelectedFiltersToAvailable(selectedDepartureAirports, departureAirportFilters.value, previousDepartureAirportFilters, shouldResetFilters)
    syncSelectedFiltersToAvailable(selectedArrivalAirports, arrivalAirportFilters.value, previousArrivalAirportFilters, shouldResetFilters)
    syncMaxDurationToAvailable(shouldResetFilters)
    hasHydratedFiltersFromUrl.value = false
  },
  { immediate: true },
)

watch(
  [
    includeDirectFlights,
    includeOneStopFlights,
    includeTwoPlusStopFlights,
    selectedProviders,
    selectedAirlines,
    selectedDepartureAirports,
    selectedArrivalAirports,
    maxDurationMinutes,
    departureTimeRange,
    arrivalTimeRange,
    returnDepartureTimeRange,
    returnArrivalTimeRange,
    selectedOutboundLegId,
    selectedReturnLegId,
  ],
  () => {
    if (currentPage.value !== 1) {
      currentPage.value = 1
    }
  },
  { deep: true },
)

watch(
  [
    includeDirectFlights,
    includeOneStopFlights,
    includeTwoPlusStopFlights,
    selectedProviders,
    selectedAirlines,
    selectedDepartureAirports,
    selectedArrivalAirports,
    maxDurationMinutes,
    departureTimeRange,
    arrivalTimeRange,
    returnDepartureTimeRange,
    returnArrivalTimeRange,
    selectedOutboundLegId,
    selectedReturnLegId,
  ],
  () => {
    scheduleSearchSessionRefresh()
  },
  { deep: true },
)

watch(
  pageTitle,
  (value) => {
    document.title = value
  },
  { immediate: true },
)

onMounted(() => {
  initializeRouteState()
})

onBeforeUnmount(() => {
  disposeSearchSession()
})

const toggleExpanded = (resultId: string) => {
  if (expandedResultIds.value.includes(resultId)) {
    expandedResultIds.value = expandedResultIds.value.filter((id) => id !== resultId)
    return
  }

  expandedResultIds.value = [...expandedResultIds.value, resultId]
}

const toggleLegFilter = ({ legId, legIndex }: { legId: string; legIndex: number }) => {
  if (legIndex === 0) {
    const isClearingSelection = selectedOutboundLegId.value === legId
    selectedOutboundLegId.value = isClearingSelection ? null : legId
    returnRanking.value = 'best'
    selectedOutboundResult.value = isClearingSelection
      ? null
      : loadedResults.value.find((result) => result.legs[0]?.id === legId) ?? null
    return
  }

  selectedReturnLegId.value = selectedReturnLegId.value === legId ? null : legId
}

const clearLegFilters = () => {
  selectedOutboundLegId.value = null
  selectedOutboundResult.value = null
  selectedReturnLegId.value = null
  returnRanking.value = 'best'
}

const updateReturnRanking = (ranking: ReturnRanking) => {
  returnRanking.value = ranking
}

const removeOriginAirport = originPicker.removeAirport
const removeDestinationAirport = destinationPicker.removeAirport
const addOriginAirport = originPicker.addAirport
const addDestinationAirport = destinationPicker.addAirport
const confirmOriginInput = originPicker.confirmInput
const confirmDestinationInput = destinationPicker.confirmInput
const swapLocations = () => {
  const previousOriginAirports = [...originAirports.value]
  const previousDestinationAirports = [...destinationAirports.value]
  const previousOriginInput = originInput.value
  const previousDestinationInput = destinationInput.value
  const previousOriginSuggestions = [...originSuggestions.value]
  const previousDestinationSuggestions = [...destinationSuggestions.value]

  originAirports.value = previousDestinationAirports
  destinationAirports.value = previousOriginAirports
  originInput.value = previousDestinationInput
  destinationInput.value = previousOriginInput
  originSuggestions.value = previousDestinationSuggestions
  destinationSuggestions.value = previousOriginSuggestions
}

</script>

<template>
  <main id="main-content" class="search-page" tabindex="-1">
    <section class="hero-panel">
      <div class="hero-copy">
        <div class="hero-heading">
          <h1>Flight discovery across nearby airports</h1>
          <p class="lead">
            Compare grouped fares, expand flexible dates, and refine results without leaving the page.
          </p>
        </div>
      </div>
    </section>

    <div class="search-bar-wrap">
      <FlightSearchBar
        v-model:origin-input="originInput"
        v-model:destination-input="destinationInput"
        v-model:origin-airports="originAirports"
        v-model:destination-airports="destinationAirports"
        v-model:trip-type="tripType"
        v-model:departure-date-from="departureDateFrom"
        v-model:departure-date-to="departureDateTo"
        v-model:selected-departure-dates="selectedDepartureDates"
        v-model:return-date-from="returnDateFrom"
        v-model:return-date-to="returnDateTo"
        v-model:selected-return-dates="selectedReturnDates"
        v-model:adults="adults"
        v-model:cabin-class="cabinClass"
        :response-exists="Boolean(response)"
        :is-collapsed="isSearchCollapsed"
        :compact-summary="compactSearchSummary"
        :search-combination-count="searchCombinationCount"
        :max-departure-range-days="MAX_DEPARTURE_RANGE_DAYS"
        :loading="loading"
        :origin-suggestions="originSuggestions"
        :destination-suggestions="destinationSuggestions"
        :cabin-options="cabinOptions"
        @submit="searchFlights"
        @toggle-collapse="isSearchCollapsed = !isSearchCollapsed"
        @confirm-origin-input="confirmOriginInput"
        @confirm-destination-input="confirmDestinationInput"
        @remove-origin-airport="removeOriginAirport"
        @remove-destination-airport="removeDestinationAirport"
        @swap-locations="swapLocations"
        @add-origin-airport="addOriginAirport"
        @add-destination-airport="addDestinationAirport"
      />
    </div>

    <p v-if="error" class="error-message" role="alert">{{ error }}</p>

    <Transition name="progress-shell">
      <SearchProgress v-if="isPolling && searchSession" :session="searchSession" />
    </Transition>

    <section class="results-grid" :class="{ 'results-only': !response }">
      <SearchFilters
        v-if="response"
        :trip-type="tripType"
        v-model:include-direct-flights="includeDirectFlights"
        v-model:include-one-stop-flights="includeOneStopFlights"
        v-model:include-two-plus-stop-flights="includeTwoPlusStopFlights"
        v-model:selected-providers="selectedProviders"
        v-model:selected-airlines="selectedAirlines"
        v-model:selected-departure-airports="selectedDepartureAirports"
        v-model:selected-arrival-airports="selectedArrivalAirports"
        v-model:departure-time-range="departureTimeRange"
        v-model:arrival-time-range="arrivalTimeRange"
        v-model:return-departure-time-range="returnDepartureTimeRange"
        v-model:return-arrival-time-range="returnArrivalTimeRange"
        v-model:max-duration-minutes="maxDurationMinutes"
        :available-max-duration-minutes="availableMaxDurationMinutes"
        :airline-filters="airlineFilters"
        :departure-airport-filters="departureAirportFilters"
        :arrival-airport-filters="arrivalAirportFilters"
        :provider-filters="providerFilters"
      />

      <SearchResultsPanel
        v-if="response"
        :trip-type="tripType"
        :response="response"
        :results="filteredResults"
        :is-polling="isPolling"
        :is-loading-more="isLoadingMore"
        :selected-outbound-leg-id="selectedOutboundLegId"
        :selected-return-leg-id="selectedReturnLegId"
        :selected-outbound-summary-result="selectedOutboundSummaryResult"
        :selected-ranking="returnRanking"
        :ranking-label="returnRankingLabel"
        :expanded-result-ids="expandedResultIds"
        :current-page="currentPage"
        :has-more-results="hasMoreResults"
        :pagination-summary="paginationSummary"
        :loaded-stop-counts="loadedStopCounts"
        @clear-leg-filters="clearLegFilters"
        @select-ranking="updateReturnRanking"
        @toggle-expanded="toggleExpanded"
        @filter-leg="toggleLegFilter"
        @load-more="loadNextPage"
      />
    </section>
  </main>
</template>

<style scoped src="./FlightSearch.css"></style>
