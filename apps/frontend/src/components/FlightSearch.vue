<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import FlightSearchBar from './flight-search/FlightSearchBar.vue'
import SearchFilters from './flight-search/SearchFilters.vue'
import SearchResultCard from './flight-search/SearchResultCard.vue'
import { fetchAirportSuggestions, getSearchSession, searchFlightsRequest } from '../features/flight-search/api'
import {
  cabinOptions,
  flexibilityOptions,
  type AirportOption,
  type SearchRequest,
  type SearchResponse,
  type SearchSessionResponse,
} from '../features/flight-search/types'

const originInput = ref('')
const destinationInput = ref('')
const originAirports = ref<AirportOption[]>([
  { code: 'DUB', name: 'Dublin', displayLabel: 'Dublin (DUB)' },
])
const destinationAirports = ref<AirportOption[]>([
  { code: 'AMS', name: 'Amsterdam Schiphol', displayLabel: 'Amsterdam Schiphol (AMS)' },
])

const departureDate = ref('2026-05-15')
const flexibleDays = ref(0)
const adults = ref(1)
const cabinClass = ref('economy')

const originSuggestions = ref<AirportOption[]>([])
const destinationSuggestions = ref<AirportOption[]>([])

const loading = ref(false)
const error = ref<string | null>(null)
const response = ref<SearchResponse | null>(null)
const searchSession = ref<SearchSessionResponse | null>(null)
const expandedResultIds = ref<string[]>([])
const isSearchCollapsed = ref(false)

const includeDirectFlights = ref(true)
const includeOneStopFlights = ref(false)
const includeTwoPlusStopFlights = ref(false)
const selectedProviders = ref<string[]>([])
const selectedAirlines = ref<string[]>([])
const priceRange = ref<[number, number]>([0, 0])
const departureTimeRange = ref<[number, number]>([0, 1439])
const arrivalTimeRange = ref<[number, number]>([0, 1439])

let originRequestId = 0
let destinationRequestId = 0
let pollingTimer: number | null = null

const providerFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return [...new Set(
    response.value.results.flatMap((result) =>
      result.priceOptions.map((option) => option.provider),
    ),
  )].sort((left, right) => left.localeCompare(right))
})

const airlineFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return [...new Set(
    response.value.results.flatMap((result) =>
      result.legs.flatMap((leg) =>
        leg.segments
          .map((segment) => segment.marketingCarrierName?.trim())
          .filter((name): name is string => Boolean(name)),
      ),
    ),
  )].sort((left, right) => left.localeCompare(right))
})

const getVisiblePriceOptions = (result: SearchResponse['results'][number]) => {
  const visibleOptions = selectedProviders.value.length > 0
    ? result.priceOptions.filter((option) => selectedProviders.value.includes(option.provider))
    : result.priceOptions

  return [...visibleOptions].sort((left, right) => left.totalPrice.amount - right.totalPrice.amount)
}

const baseFilteredResults = computed(() => {
  if (!response.value) {
    return []
  }

  return response.value.results.flatMap((result) => {
    const stopCount = getStopCount(result)
    const includeByStopCount = (
      (stopCount === 0 && includeDirectFlights.value) ||
      (stopCount === 1 && includeOneStopFlights.value) ||
      (stopCount >= 2 && includeTwoPlusStopFlights.value)
    )

    if (!includeByStopCount) {
      return []
    }

    const visiblePriceOptions = getVisiblePriceOptions(result)
    if (visiblePriceOptions.length === 0) {
      return []
    }

    if (selectedAirlines.value.length > 0) {
      const resultAirlines = new Set(
        result.legs.flatMap((leg) =>
          leg.segments
            .map((segment) => segment.marketingCarrierName?.trim())
            .filter((name): name is string => Boolean(name)),
        ),
      )

      const hasSelectedAirline = selectedAirlines.value.some((airline) => resultAirlines.has(airline))
      if (!hasSelectedAirline) {
        return []
      }
    }

    const firstDepartureMinutes = getUtcMinutes(result.legs[0]?.departureUtc)
    if (firstDepartureMinutes < departureTimeRange.value[0] || firstDepartureMinutes > departureTimeRange.value[1]) {
      return []
    }

    const lastArrivalMinutes = getUtcMinutes(result.legs[result.legs.length - 1]?.arrivalUtc)
    if (lastArrivalMinutes < arrivalTimeRange.value[0] || lastArrivalMinutes > arrivalTimeRange.value[1]) {
      return []
    }

    return [{
      ...result,
      priceOptions: visiblePriceOptions,
    }]
  })
})

const availablePriceRange = computed<[number, number]>(() => {
  if (baseFilteredResults.value.length === 0) {
    return [0, 0]
  }

  const amounts = baseFilteredResults.value
    .map((result) => result.priceOptions[0]?.totalPrice.amount ?? 0)
    .filter((amount) => Number.isFinite(amount))

  if (amounts.length === 0) {
    return [0, 0]
  }

  return [Math.floor(Math.min(...amounts)), Math.ceil(Math.max(...amounts))]
})

const filteredResults = computed(() => {
  return baseFilteredResults.value.filter((result) => {
    const cheapest = result.priceOptions[0]
    if (cheapest.totalPrice.amount < priceRange.value[0] || cheapest.totalPrice.amount > priceRange.value[1]) {
      return false
    }

    return true
  })
})

const searchSummary = computed(() => {
  if (!response.value) {
    return 'Search across multiple airports and compare grouped fares.'
  }

  return `${filteredResults.value.length} flights after filters`
})

const progressPercentage = computed(() => {
  if (!searchSession.value || searchSession.value.totalCombinations === 0) {
    return 0
  }

  return Math.round((searchSession.value.completedCombinations / searchSession.value.totalCombinations) * 100)
})

const isPolling = computed(() =>
  searchSession.value?.status === 'running',
)

const compactSearchSummary = computed(() => {
  const origins = originAirports.value.map((airport) => airport.code).join(', ')
  const destinations = destinationAirports.value.map((airport) => airport.code).join(', ')
  const flexibilityLabel = flexibleDays.value === 0 ? 'Exact' : `±${flexibleDays.value}d`
  return `${origins} to ${destinations} on ${departureDate.value} (${flexibilityLabel})`
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
    return `Aveon · ${filteredResults.value.length} flights from ${routeSummary}`
  }

  return 'Aveon'
})

const searchCombinationCount = computed(() => {
  const origins = uniqueAirportCodes(originAirports.value)
  const destinations = uniqueAirportCodes(destinationAirports.value)
  const departureDateCount = (flexibleDays.value * 2) + 1

  if (origins.length === 0 || destinations.length === 0 || departureDateCount <= 0) {
    return 0
  }

  const routeCombinationCount = origins.reduce((count, origin) => (
    count + destinations.filter((destination) => destination !== origin).length
  ), 0)

  return routeCombinationCount * departureDateCount
})

const addDays = (dateString: string, days: number) => {
  const date = new Date(`${dateString}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

const getUtcMinutes = (value?: string) => {
  if (!value) {
    return 0
  }

  const date = new Date(value)
  return (date.getUTCHours() * 60) + date.getUTCMinutes()
}

const getStopCount = (result: SearchResponse['results'][number]) =>
  result.legs.reduce((totalStops, leg) => totalStops + Math.max(leg.segments.length - 1, 0), 0)

const isSelected = (items: AirportOption[], code: string) =>
  items.some((item) => item.code === code)

const removeAirport = (
  items: typeof originAirports | typeof destinationAirports,
  code: string,
) => {
  items.value = items.value.filter((item) => item.code !== code)
}

const addAirport = (
  items: typeof originAirports | typeof destinationAirports,
  input: typeof originInput | typeof destinationInput,
  suggestions: typeof originSuggestions | typeof destinationSuggestions,
  airport: AirportOption,
) => {
  if (isSelected(items.value, airport.code)) {
    input.value = ''
    suggestions.value = []
    return
  }

  items.value = [...items.value, airport]
  input.value = ''
  suggestions.value = []
}

const tryAddFromInput = (
  items: typeof originAirports | typeof destinationAirports,
  input: typeof originInput | typeof destinationInput,
  suggestions: typeof originSuggestions | typeof destinationSuggestions,
) => {
  const trimmed = input.value.trim()
  if (!trimmed) {
    return
  }

  const match = suggestions.value.find((option) =>
    option.code.toLowerCase() === trimmed.toLowerCase() ||
    option.displayLabel.toLowerCase() === trimmed.toLowerCase(),
  )

  if (match) {
    addAirport(items, input, suggestions, match)
  }
}

const updateSuggestions = async (
  query: string,
  items: typeof originAirports | typeof destinationAirports,
  suggestions: typeof originSuggestions | typeof destinationSuggestions,
  requestId: number,
  getLatestRequestId: () => number,
) => {
  const trimmed = query.trim()
  if (trimmed.length < 2) {
    suggestions.value = []
    return
  }

  try {
    const lookup = await fetchAirportSuggestions(trimmed)
    if (requestId !== getLatestRequestId()) {
      return
    }

    suggestions.value = lookup.filter((airport) => !isSelected(items.value, airport.code))
  } catch {
    if (requestId === getLatestRequestId()) {
      suggestions.value = []
    }
  }
}

const resetPriceRangeToAvailable = () => {
  const [minPrice, maxPrice] = availablePriceRange.value
  priceRange.value = [minPrice, maxPrice]
}

const resetSelectedProvidersToAvailable = () => {
  selectedProviders.value = [...providerFilters.value]
}

const resetSelectedAirlinesToAvailable = () => {
  selectedAirlines.value = [...airlineFilters.value]
}

watch(originInput, (value) => {
  originRequestId += 1
  void updateSuggestions(value, originAirports, originSuggestions, originRequestId, () => originRequestId)
})

watch(destinationInput, (value) => {
  destinationRequestId += 1
  void updateSuggestions(value, destinationAirports, destinationSuggestions, destinationRequestId, () => destinationRequestId)
})

watch(
  [
    response,
    includeDirectFlights,
    includeOneStopFlights,
    includeTwoPlusStopFlights,
    selectedProviders,
    departureTimeRange,
    arrivalTimeRange,
  ],
  () => {
    resetPriceRangeToAvailable()
  },
  { immediate: true, deep: true },
)

watch(
  response,
  () => {
    resetSelectedProvidersToAvailable()
    resetSelectedAirlinesToAvailable()
  },
  { immediate: true },
)

watch(
  pageTitle,
  (value) => {
    document.title = value
  },
  { immediate: true },
)

onMounted(() => {
  void fetchAirportSuggestions(originAirports.value[0].code)
  void fetchAirportSuggestions(destinationAirports.value[0].code)
})

onBeforeUnmount(() => {
  stopPolling()
})

const stopPolling = () => {
  if (pollingTimer !== null) {
    window.clearTimeout(pollingTimer)
    pollingTimer = null
  }
}

const pollSearchSession = async (searchId: string) => {
  try {
    const session = await getSearchSession(searchId)
    searchSession.value = session
    response.value = session.response

    if (session.errorMessage) {
      error.value = session.errorMessage
    }

    if (session.status === 'running') {
      pollingTimer = window.setTimeout(() => {
        void pollSearchSession(searchId)
      }, 1000)
      return
    }

    stopPolling()
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Unknown error'
    stopPolling()
  }
}

const searchFlights = async () => {
  stopPolling()
  loading.value = true
  error.value = null
  response.value = null
  searchSession.value = null
  expandedResultIds.value = []

  try {
    const request: SearchRequest = {
      originAirports: originAirports.value.map((airport) => airport.code),
      destinationAirports: destinationAirports.value.map((airport) => airport.code),
      departDateFrom: addDays(departureDate.value, -flexibleDays.value),
      departDateTo: addDays(departureDate.value, flexibleDays.value),
      returnDateFrom: null,
      returnDateTo: null,
      adults: adults.value,
      cabinClass: cabinClass.value,
    }

    const session = await searchFlightsRequest(request)
    searchSession.value = session
    response.value = session.response
    isSearchCollapsed.value = true
    loading.value = false

    if (session.status === 'running') {
      await pollSearchSession(session.searchId)
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Unknown error'
  } finally {
    if (!isPolling.value) {
      loading.value = false
    }
  }
}

const toggleExpanded = (resultId: string) => {
  if (expandedResultIds.value.includes(resultId)) {
    expandedResultIds.value = expandedResultIds.value.filter((id) => id !== resultId)
    return
  }

  expandedResultIds.value = [...expandedResultIds.value, resultId]
}

const isExpanded = (resultId: string) => expandedResultIds.value.includes(resultId)

const removeOriginAirport = (code: string) => removeAirport(originAirports, code)
const removeDestinationAirport = (code: string) => removeAirport(destinationAirports, code)
const addOriginAirport = (airport: AirportOption) => addAirport(originAirports, originInput, originSuggestions, airport)
const addDestinationAirport = (airport: AirportOption) => addAirport(destinationAirports, destinationInput, destinationSuggestions, airport)
const confirmOriginInput = () => tryAddFromInput(originAirports, originInput, originSuggestions)
const confirmDestinationInput = () => tryAddFromInput(destinationAirports, destinationInput, destinationSuggestions)
</script>

<template>
  <main class="search-page">
    <section class="hero-panel">
      <div class="hero-copy">
        <p class="eyebrow">Aveon</p>
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
        v-model:departure-date="departureDate"
        v-model:flexible-days="flexibleDays"
        v-model:adults="adults"
        v-model:cabin-class="cabinClass"
        :response-exists="Boolean(response)"
        :is-collapsed="isSearchCollapsed"
        :compact-summary="compactSearchSummary"
        :search-combination-count="searchCombinationCount"
        :loading="loading"
        :origin-suggestions="originSuggestions"
        :destination-suggestions="destinationSuggestions"
        :cabin-options="cabinOptions"
        :flexibility-options="flexibilityOptions"
        @submit="searchFlights"
        @toggle-collapse="isSearchCollapsed = !isSearchCollapsed"
        @confirm-origin-input="confirmOriginInput"
        @confirm-destination-input="confirmDestinationInput"
        @remove-origin-airport="removeOriginAirport"
        @remove-destination-airport="removeDestinationAirport"
        @add-origin-airport="addOriginAirport"
        @add-destination-airport="addDestinationAirport"
      />
    </div>

    <p v-if="error" class="error-message">{{ error }}</p>

    <Transition name="progress-shell">
      <section v-if="isPolling && searchSession" class="progress-shell">
        <div class="progress-copy">
          <p class="eyebrow">Search Progress</p>
          <strong>
            {{ searchSession.completedCombinations }} / {{ searchSession.totalCombinations }} combinations
          </strong>
          <span v-if="searchSession.failedCombinations > 0">
            {{ searchSession.failedCombinations }} failed
          </span>
        </div>
        <div class="progress-bar">
          <div class="progress-bar-fill" :style="{ width: `${progressPercentage}%` }" />
        </div>
      </section>
    </Transition>

    <section class="results-grid" :class="{ 'results-only': !response }">
      <SearchFilters
        v-if="response"
        v-model:price-range="priceRange"
        v-model:include-direct-flights="includeDirectFlights"
        v-model:include-one-stop-flights="includeOneStopFlights"
        v-model:include-two-plus-stop-flights="includeTwoPlusStopFlights"
        v-model:selected-providers="selectedProviders"
        v-model:selected-airlines="selectedAirlines"
        v-model:departure-time-range="departureTimeRange"
        v-model:arrival-time-range="arrivalTimeRange"
        :available-price-range="availablePriceRange"
        :airline-filters="airlineFilters"
        :provider-filters="providerFilters"
      />

      <section class="results-panel">
        <div v-if="response" class="results-shell">
          <div class="results-header">
            <div>
              <p class="eyebrow">Results</p>
              <h2>{{ searchSummary }}</h2>
            </div>
            <div class="results-stats">
              <span
                :title="`Flight options: ${response.metadata.returnedResultCount}\nDirect: ${response.metadata.returnedDirectFlightCount}\n1 stop: ${response.metadata.returnedOneStopFlightCount}\n2+ stops: ${response.metadata.returnedTwoPlusStopFlightCount}`"
              >
                {{ response.metadata.returnedResultCount }} flight options
              </span>
              <span>{{ response.metadata.providerResultCount }} provider fares</span>
              <span>{{ response.metadata.searchCombinationCount }} search combinations</span>
            </div>
          </div>

          <TransitionGroup name="result-list" tag="div" class="results-list">
            <SearchResultCard
              v-for="result in filteredResults"
              :key="result.id"
              :result="result"
              :expanded="isExpanded(result.id)"
              @toggle-expanded="toggleExpanded"
            />
          </TransitionGroup>
        </div>
      </section>
    </section>
  </main>
</template>

<style scoped src="./FlightSearch.css"></style>
