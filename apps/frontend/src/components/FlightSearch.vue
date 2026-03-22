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

const directFlightsOnly = ref(true)
const selectedProviders = ref<string[]>([])
const maxPrice = ref(800)

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

const filteredResults = computed(() => {
  if (!response.value) {
    return []
  }

  return response.value.results.filter((result) => {
    const cheapest = result.priceOptions[0]
    if (cheapest.totalPrice.amount > maxPrice.value) {
      return false
    }

    if (directFlightsOnly.value && result.legs.some((leg) => leg.segments.length > 1)) {
      return false
    }

    if (selectedProviders.value.length > 0) {
      return result.priceOptions.some((option) => selectedProviders.value.includes(option.provider))
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

const addDays = (dateString: string, days: number) => {
  const date = new Date(`${dateString}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

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

watch(originInput, (value) => {
  originRequestId += 1
  void updateSuggestions(value, originAirports, originSuggestions, originRequestId, () => originRequestId)
})

watch(destinationInput, (value) => {
  destinationRequestId += 1
  void updateSuggestions(value, destinationAirports, destinationSuggestions, destinationRequestId, () => destinationRequestId)
})

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
      <div>
        <p class="eyebrow">Aveon</p>
        <h1>Flight discovery with a wider canvas.</h1>
        <p class="lead">
          Search multiple airports, compare grouped fares, and refine results from a
          dedicated filter rail.
        </p>
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
        v-model:max-price="maxPrice"
        v-model:direct-flights-only="directFlightsOnly"
        v-model:selected-providers="selectedProviders"
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

<style scoped>
.search-page {
  min-height: 100vh;
  padding: 18px 18px 28px;
  background:
    radial-gradient(circle at top left, rgba(255, 196, 61, 0.22), transparent 24%),
    radial-gradient(circle at top right, rgba(44, 123, 229, 0.18), transparent 26%),
    linear-gradient(180deg, #f5f1e8 0%, #fbfaf7 42%, #eeece5 100%);
  color: #1d2228;
}

.hero-panel,
.search-bar-wrap,
.progress-shell,
.results-grid,
.error-message {
  width: min(1520px, 100%);
  margin: 0 auto;
}

.hero-panel {
  margin-bottom: 8px;
}

.eyebrow {
  margin: 0 0 8px;
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: #9c5a11;
}

h1,
h2 {
  margin: 0;
  color: #1d2228;
}

h1 {
  max-width: 12ch;
  font-family: Georgia, 'Times New Roman', serif;
  font-size: clamp(2rem, 4vw, 3.2rem);
  line-height: 1;
  letter-spacing: -0.05em;
}

.lead {
  max-width: 58rem;
  margin: 8px 0 0;
  font-size: 0.9rem;
  line-height: 1.45;
  color: #4b5661;
}

.error-message {
  margin-top: 0;
  margin-bottom: 12px;
  padding: 12px 14px;
  border-radius: 14px;
  background: #fff0ef;
  color: #a53a2a;
  border: 1px solid rgba(165, 58, 42, 0.16);
}

.progress-shell {
  margin: 0 auto 10px;
  padding: 10px 12px;
  border: 1px solid rgba(29, 34, 40, 0.08);
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.88);
  box-shadow: 0 8px 20px rgba(41, 49, 61, 0.04);
}

.progress-copy {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}

.progress-copy strong {
  font-size: 0.95rem;
}

.progress-copy span {
  color: #5b6570;
}

.progress-bar {
  height: 8px;
  border-radius: 999px;
  background: #e8edf3;
  overflow: hidden;
}

.progress-bar-fill {
  height: 100%;
  border-radius: 999px;
  background: linear-gradient(135deg, #1f5fbf 0%, #2c7be5 100%);
  transition: width 0.4s ease;
}

.results-grid {
  display: grid;
  grid-template-columns: 220px minmax(0, 1fr);
  gap: 12px;
  align-items: start;
}

.results-grid.results-only {
  grid-template-columns: 1fr;
}

.results-shell {
  border: 1px solid rgba(29, 34, 40, 0.08);
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.88);
  box-shadow: 0 10px 28px rgba(41, 49, 61, 0.05);
  backdrop-filter: blur(18px);
  padding: 14px;
}

.results-header {
  display: flex;
  justify-content: space-between;
  align-items: end;
  gap: 10px;
  margin-bottom: 10px;
}

.results-stats {
  display: flex;
  flex-wrap: wrap;
  justify-content: end;
  gap: 6px;
  color: #5b6570;
  font-size: 0.82rem;
}

.results-stats span {
  padding: 5px 8px;
  border-radius: 999px;
  background: #eef2f6;
}

.results-list {
  display: grid;
  gap: 16px;
}

.progress-shell-enter-active,
.progress-shell-leave-active {
  transition:
    opacity 0.28s ease,
    transform 0.28s ease,
    max-height 0.28s ease,
    margin-bottom 0.28s ease;
  overflow: hidden;
}

.progress-shell-enter-from,
.progress-shell-leave-to {
  opacity: 0;
  transform: translateY(-8px);
  max-height: 0;
  margin-bottom: 0;
}

.progress-shell-enter-to,
.progress-shell-leave-from {
  opacity: 1;
  transform: translateY(0);
  max-height: 120px;
  margin-bottom: 10px;
}

.result-list-enter-active,
.result-list-leave-active {
  transition:
    opacity 0.16s ease,
    transform 0.16s ease;
}

.result-list-move {
  transition: transform 0.16s ease;
}

.result-list-enter-from {
  opacity: 0;
  transform: translateY(6px) scale(0.995);
}

.result-list-leave-to {
  opacity: 0;
  transform: translateY(-4px) scale(0.995);
}

@media (max-width: 960px) {
  .results-grid {
    grid-template-columns: 1fr;
  }

  .results-header {
    align-items: start;
    flex-direction: column;
  }

  .results-stats {
    justify-content: start;
  }
}
</style>
