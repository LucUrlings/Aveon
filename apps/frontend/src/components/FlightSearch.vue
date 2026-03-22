<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'

type AirportOption = {
  code: string
  name: string | null
  displayLabel: string
}

type SearchResultSegment = {
  marketingCarrierName: string
  marketingCarrierCode: string
  flightNumber: string
  originAirport: string
  destinationAirport: string
  departureUtc: string
  arrivalUtc: string
  durationMinutes: number
}

type SearchResultLeg = {
  originAirport: string
  destinationAirport: string
  departureUtc: string
  arrivalUtc: string
  durationMinutes: number
  segments: SearchResultSegment[]
}

type SearchResultPriceOption = {
  id: string
  provider: string
  totalPrice: {
    amount: number
    currency: string
  }
  deepLink: string
}

type SearchResult = {
  id: string
  isRoundTrip: boolean
  legs: SearchResultLeg[]
  totalDurationMinutes: number
  priceOptions: SearchResultPriceOption[]
}

type SearchResponse = {
  results: SearchResult[]
  metadata: {
    searchCombinationCount: number
    providerResultCount: number
    returnedResultCount: number
    returnedDirectFlightCount: number
    returnedOneStopFlightCount: number
    returnedTwoPlusStopFlightCount: number
  }
}

type AirportLookupResponse = {
  airports: AirportOption[]
}

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
const expandedResultIds = ref<string[]>([])
const isSearchCollapsed = ref(false)

const directFlightsOnly = ref(true)
const selectedProviders = ref<string[]>([])
const maxPrice = ref(800)

const cabinOptions = [
  { label: 'Economy', value: 'economy' },
  { label: 'Business', value: 'business' },
  { label: 'First', value: 'first' },
  { label: 'Premium Economy', value: 'premium_economy' },
]

const flexibilityOptions = [
  { label: 'Exact', value: 0 },
  { label: '±1 day', value: 1 },
  { label: '±2 days', value: 2 },
  { label: '±3 days', value: 3 },
]

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

let originRequestId = 0
let destinationRequestId = 0

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

const compactSearchSummary = computed(() => {
  const origins = originAirports.value.map((airport) => airport.code).join(', ')
  const destinations = destinationAirports.value.map((airport) => airport.code).join(', ')
  const flexibilityLabel = flexibleDays.value === 0 ? 'Exact' : `±${flexibleDays.value}d`
  return `${origins} to ${destinations} on ${departureDate.value} (${flexibilityLabel})`
})

const formatDateTime = (value: string) =>
  {
    const date = new Date(value)
    const weekday = new Intl.DateTimeFormat('en-IE', {
      weekday: 'short',
      timeZone: 'UTC',
    }).format(date).slice(0, 2)

    const rest = new Intl.DateTimeFormat('en-IE', {
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
      timeZone: 'UTC',
    }).format(date)

    return `${weekday} ${rest}`
  }

const formatDuration = (totalMinutes: number) => {
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  return `${hours}h ${minutes}m`
}

const addDays = (dateString: string, days: number) => {
  const date = new Date(`${dateString}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

const formatProviderName = (provider: string) => provider.replace(/^FlightApi:/, '').trim()

const getAirlineSummary = (result: SearchResult) => {
  const airlines = [...new Set(
    result.legs.flatMap((leg) =>
      leg.segments.map((segment) => segment.marketingCarrierName),
    ),
  )].filter(Boolean)

  return airlines.join(', ') || 'Unknown airline'
}

const isSelected = (items: AirportOption[], code: string) =>
  items.some((item) => item.code === code)

const removeAirport = (items: typeof originAirports, code: string) => {
  items.value = items.value.filter((item) => item.code !== code)
}

const addAirport = (
  items: typeof originAirports,
  input: typeof originInput,
  suggestions: typeof originSuggestions,
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
  items: typeof originAirports,
  input: typeof originInput,
  suggestions: typeof originSuggestions,
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

const fetchAirports = async (query: string) => {
  const res = await fetch(`http://localhost:5200/api/v1/airports?query=${encodeURIComponent(query)}`)
  if (!res.ok) {
    throw new Error(`Airport lookup failed with HTTP ${res.status}`)
  }

  return (await res.json()) as AirportLookupResponse
}

const updateSuggestions = async (
  query: string,
  items: typeof originAirports,
  suggestions: typeof originSuggestions,
  requestId: number,
  getLatestRequestId: () => number,
) => {
  const trimmed = query.trim()
  if (trimmed.length < 2) {
    suggestions.value = []
    return
  }

  try {
    const lookup = await fetchAirports(trimmed)
    if (requestId !== getLatestRequestId()) {
      return
    }

    suggestions.value = lookup.airports.filter((airport) => !isSelected(items.value, airport.code))
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
  void fetchAirports(originAirports.value[0].code)
  void fetchAirports(destinationAirports.value[0].code)
})

const searchFlights = async () => {
  loading.value = true
  error.value = null
  response.value = null
  expandedResultIds.value = []

  try {
    const res = await fetch('http://localhost:5200/api/v1/search', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        originAirports: originAirports.value.map((airport) => airport.code),
        destinationAirports: destinationAirports.value.map((airport) => airport.code),
        departDateFrom: addDays(departureDate.value, -flexibleDays.value),
        departDateTo: addDays(departureDate.value, flexibleDays.value),
        returnDateFrom: null,
        returnDateTo: null,
        adults: adults.value,
        cabinClass: cabinClass.value,
      }),
    })

    if (!res.ok) {
      const message = await res.text()
      throw new Error(message || `HTTP ${res.status}`)
    }

    response.value = (await res.json()) as SearchResponse
    isSearchCollapsed.value = true
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Unknown error'
  } finally {
    loading.value = false
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

    <section class="search-shell" :class="{ collapsed: isSearchCollapsed }">
      <div class="search-shell-header">
        <div>
          <p class="eyebrow">Search</p>
          <h2>{{ isSearchCollapsed ? compactSearchSummary : 'Build a one-way search' }}</h2>
        </div>
        <button
          v-if="response"
          type="button"
          class="collapse-toggle"
          @click="isSearchCollapsed = !isSearchCollapsed"
        >
          {{ isSearchCollapsed ? 'Edit search' : 'Collapse' }}
        </button>
      </div>

      <form v-show="!isSearchCollapsed" class="search-form" @submit.prevent="searchFlights">
        <div class="search-layout">
          <div class="airport-grid">
            <div class="field">
              <span>Origin airports</span>
              <div class="airport-picker">
                <div class="chip-row">
                  <button
                    v-for="airport in originAirports"
                    :key="airport.code"
                    type="button"
                    class="airport-chip"
                    @click="removeOriginAirport(airport.code)"
                  >
                    {{ airport.code }}
                  </button>
                </div>
                <input
                  v-model="originInput"
                  placeholder="Add airport or city"
                  @keydown.enter.prevent="confirmOriginInput"
                />
                <ul v-if="originSuggestions.length" class="suggestions-list">
                  <li v-for="airport in originSuggestions" :key="airport.code">
                    <button
                      type="button"
                      class="suggestion-button"
                      @click="addOriginAirport(airport)"
                    >
                      {{ airport.displayLabel }}
                    </button>
                  </li>
                </ul>
              </div>
            </div>

            <div class="field">
              <span>Destination airports</span>
              <div class="airport-picker">
                <div class="chip-row">
                  <button
                    v-for="airport in destinationAirports"
                    :key="airport.code"
                    type="button"
                    class="airport-chip"
                    @click="removeDestinationAirport(airport.code)"
                  >
                    {{ airport.code }}
                  </button>
                </div>
                <input
                  v-model="destinationInput"
                  placeholder="Add airport or city"
                  @keydown.enter.prevent="confirmDestinationInput"
                />
                <ul v-if="destinationSuggestions.length" class="suggestions-list">
                  <li v-for="airport in destinationSuggestions" :key="airport.code">
                    <button
                      type="button"
                      class="suggestion-button"
                      @click="addDestinationAirport(airport)"
                    >
                      {{ airport.displayLabel }}
                    </button>
                  </li>
                </ul>
              </div>
            </div>
          </div>

          <div class="settings-grid">
            <label class="field">
              <span>Departure date</span>
              <input v-model="departureDate" type="date" />
            </label>

            <label class="field">
              <span>Flexible days</span>
              <select v-model.number="flexibleDays">
                <option
                  v-for="option in flexibilityOptions"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </option>
              </select>
            </label>

            <label class="field field-compact">
              <span>Adults</span>
              <input v-model.number="adults" type="number" min="1" max="9" />
            </label>

            <label class="field">
              <span>Cabin class</span>
              <select v-model="cabinClass">
                <option v-for="option in cabinOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </option>
              </select>
            </label>
          </div>
        </div>

        <button class="search-button" type="submit" :disabled="loading">
          {{ loading ? 'Searching...' : 'Search flights' }}
        </button>
      </form>
    </section>

    <p v-if="error" class="error-message">{{ error }}</p>

    <section class="results-grid" :class="{ 'results-only': !response }">
      <aside v-if="response" class="filters-panel">
        <div class="filters-card">
          <p class="eyebrow">Filters</p>
          <h3>Refine results</h3>

          <label class="filter-row">
            <span>Max price</span>
            <input v-model.number="maxPrice" type="range" min="50" max="1500" step="10" />
            <strong>EUR {{ maxPrice }}</strong>
          </label>

          <label class="filter-toggle">
            <input v-model="directFlightsOnly" type="checkbox" />
            <span>Direct flights</span>
          </label>

          <div v-if="providerFilters.length" class="provider-filter-group">
            <span class="filter-label">Preferred providers</span>
            <label
              v-for="provider in providerFilters"
              :key="provider"
              class="filter-toggle"
            >
              <input v-model="selectedProviders" :value="provider" type="checkbox" />
              <span>{{ provider.replace('FlightApi:', '') }}</span>
            </label>
          </div>
        </div>
      </aside>

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

          <article v-for="result in filteredResults" :key="result.id" class="result-card">
            <div class="details-header">
              <p class="provider">{{ getAirlineSummary(result) }}</p>
              <p class="route">
                {{ result.legs[0]?.originAirport }} to
                {{ result.legs[result.legs.length - 1]?.destinationAirport }}
              </p>
            </div>

            <div v-for="(leg, legIndex) in result.legs" :key="`${result.id}-${legIndex}`" class="leg-block">
              <div class="leg-summary">
                <div>
                  <p>{{ leg.originAirport }} → {{ leg.destinationAirport }}</p>
                  <span>{{ formatDateTime(leg.departureUtc) }} to {{ formatDateTime(leg.arrivalUtc) }}</span>
                </div>
                <strong>{{ formatDuration(leg.durationMinutes) }}</strong>
              </div>

              <ul class="segment-list">
                <li
                  v-for="segment in leg.segments"
                  :key="segment.flightNumber + segment.departureUtc"
                  class="segment-item"
                >
                  <span>{{ segment.marketingCarrierName }} ({{ segment.marketingCarrierCode }}) {{ segment.flightNumber }}</span>
                  <span>{{ segment.originAirport }} → {{ segment.destinationAirport }}</span>
                  <span>{{ formatDateTime(segment.departureUtc) }} to {{ formatDateTime(segment.arrivalUtc) }}</span>
                </li>
              </ul>
            </div>

            <div class="fare-stack">
              <div class="fare-summary">
                <div class="fare-provider">
                  <span class="fare-provider-label">{{ formatProviderName(result.priceOptions[0].provider) }}</span>
                  <span>{{ formatDuration(result.totalDurationMinutes) }}</span>
                </div>
                <div class="price-block">
                  <strong>
                    {{ result.priceOptions[0].totalPrice.currency }}
                    {{ result.priceOptions[0].totalPrice.amount.toFixed(2) }}
                  </strong>
                  <a
                    v-if="result.priceOptions[0].deepLink"
                    class="primary-fare-link"
                    :href="result.priceOptions[0].deepLink"
                    target="_blank"
                    rel="noreferrer"
                  >
                    View fare
                  </a>
                </div>
              </div>

              <button
                v-if="result.priceOptions.length > 1"
                class="expand-button attached-expand"
                type="button"
                @click="toggleExpanded(result.id)"
              >
                {{ isExpanded(result.id) ? 'Hide other fares' : `Show ${result.priceOptions.length - 1} more fares` }}
              </button>
            </div>

            <div v-if="result.priceOptions.length > 1 && isExpanded(result.id)" class="other-fares">
              <p class="other-fares-title">Other fare options</p>
              <ul class="other-fares-list">
                <li
                  v-for="option in result.priceOptions.slice(1)"
                  :key="option.id"
                  class="other-fare-item"
                >
                  <div>
                    <strong>{{ formatProviderName(option.provider) }}</strong>
                    <span>{{ option.totalPrice.currency }} {{ option.totalPrice.amount.toFixed(2) }}</span>
                  </div>
                  <a
                    v-if="option.deepLink"
                    :href="option.deepLink"
                    target="_blank"
                    rel="noreferrer"
                  >
                    View fare
                  </a>
                </li>
              </ul>
            </div>
          </article>
        </div>
      </section>
    </section>
  </main>
</template>

<style scoped>
.search-page {
  min-height: 100vh;
  padding: 38px 24px 72px;
  background:
    radial-gradient(circle at top left, rgba(255, 196, 61, 0.22), transparent 24%),
    radial-gradient(circle at top right, rgba(44, 123, 229, 0.18), transparent 26%),
    linear-gradient(180deg, #f5f1e8 0%, #fbfaf7 42%, #eeece5 100%);
  color: #1d2228;
}

.hero-panel,
.search-shell,
.results-grid,
.error-message {
  width: min(1520px, 100%);
  margin: 0 auto;
}

.hero-panel {
  margin-bottom: 18px;
}

.eyebrow {
  margin: 0 0 10px;
  font-size: 0.78rem;
  font-weight: 700;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: #9c5a11;
}

h1,
h2,
h3 {
  margin: 0;
  color: #1d2228;
}

h1 {
  max-width: 12ch;
  font-family: Georgia, 'Times New Roman', serif;
  font-size: clamp(3rem, 7vw, 5.2rem);
  line-height: 0.95;
  letter-spacing: -0.05em;
}

.lead {
  max-width: 58rem;
  margin: 16px 0 0;
  font-size: 1.06rem;
  line-height: 1.7;
  color: #4b5661;
}

.search-shell,
.filters-card,
.results-shell {
  border: 1px solid rgba(29, 34, 40, 0.08);
  border-radius: 28px;
  background: rgba(255, 255, 255, 0.88);
  box-shadow: 0 24px 60px rgba(41, 49, 61, 0.08);
  backdrop-filter: blur(18px);
}

.search-shell {
  padding: 20px 22px;
  margin-bottom: 18px;
}

.search-shell.collapsed {
  padding-bottom: 18px;
}

.search-shell-header {
  display: flex;
  justify-content: space-between;
  align-items: start;
  gap: 16px;
  margin-bottom: 10px;
}

.collapse-toggle {
  border: 1px solid rgba(29, 34, 40, 0.12);
  border-radius: 999px;
  padding: 10px 14px;
  background: #fff;
  font: inherit;
  font-weight: 700;
  cursor: pointer;
}

.search-form {
  margin-top: 10px;
}

.search-layout {
  display: grid;
  gap: 16px;
}

.airport-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18px;
}

.settings-grid {
  display: grid;
  grid-template-columns: minmax(220px, 0.8fr) minmax(170px, 0.55fr) minmax(120px, 0.3fr) minmax(180px, 0.45fr);
  gap: 14px;
  max-width: 980px;
}

.field {
  display: grid;
  gap: 8px;
}

.field span,
.filter-label {
  font-size: 0.82rem;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: #5b6570;
}

.airport-picker {
  position: relative;
  display: grid;
  gap: 10px;
}

.chip-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  min-height: 38px;
}

.airport-chip {
  border: none;
  border-radius: 999px;
  padding: 8px 12px;
  background: #eef4fb;
  color: #1f5fbf;
  font-weight: 700;
  cursor: pointer;
}

.airport-picker input,
.field input,
.field select {
  width: 100%;
  box-sizing: border-box;
  border: 1px solid #d5dbe1;
  border-radius: 16px;
  padding: 14px 16px;
  font: inherit;
  background: #fff;
  color: #1d2228;
}

.airport-picker input:focus,
.field input:focus,
.field select:focus {
  outline: none;
  border-color: #2c7be5;
  box-shadow: 0 0 0 4px rgba(44, 123, 229, 0.14);
}

.field-compact {
  max-width: 140px;
}

.suggestions-list {
  list-style: none;
  padding: 8px;
  margin: 0;
  border: 1px solid rgba(29, 34, 40, 0.08);
  border-radius: 18px;
  background: #fff;
  box-shadow: 0 20px 45px rgba(41, 49, 61, 0.12);
}

.suggestion-button {
  width: 100%;
  border: none;
  background: transparent;
  text-align: left;
  padding: 10px 12px;
  border-radius: 12px;
  cursor: pointer;
}

.suggestion-button:hover {
  background: #f2f5f8;
}

.search-button {
  margin-top: 18px;
  border: none;
  border-radius: 999px;
  padding: 14px 24px;
  font: inherit;
  font-weight: 700;
  color: #fff;
  background: linear-gradient(135deg, #1f5fbf 0%, #2c7be5 100%);
  cursor: pointer;
}

.search-button:disabled {
  opacity: 0.7;
  cursor: wait;
}

.error-message {
  margin-top: 0;
  margin-bottom: 18px;
  padding: 14px 16px;
  border-radius: 16px;
  background: #fff0ef;
  color: #a53a2a;
  border: 1px solid rgba(165, 58, 42, 0.16);
}

.results-grid {
  display: grid;
  grid-template-columns: 280px minmax(0, 1fr);
  gap: 20px;
  align-items: start;
}

.results-grid.results-only {
  grid-template-columns: 1fr;
}

.filters-panel {
  position: sticky;
  top: 20px;
}

.filters-card {
  padding: 22px 18px;
}

.filters-card h3 {
  margin-bottom: 18px;
  font-size: 1.25rem;
}

.filter-row {
  display: grid;
  gap: 10px;
  margin-bottom: 18px;
}

.filter-toggle {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 10px;
}

.provider-filter-group {
  padding-top: 14px;
  border-top: 1px solid rgba(29, 34, 40, 0.08);
}

.results-shell {
  padding: 24px;
}

.results-header {
  display: flex;
  justify-content: space-between;
  align-items: end;
  gap: 16px;
  margin-bottom: 20px;
}

.results-header h2 {
  font-size: clamp(1.5rem, 3vw, 2.1rem);
}

.results-stats {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.results-stats span {
  padding: 10px 12px;
  border-radius: 999px;
  background: #eef4fb;
  color: #36506b;
  font-size: 0.92rem;
}

.result-card {
  padding: 20px 0;
  border-top: 1px solid rgba(29, 34, 40, 0.08);
}

.result-card:first-of-type {
  border-top: none;
  padding-top: 0;
}

.details-header,
.leg-summary,
.segment-item,
.fare-summary,
.other-fare-item {
  display: flex;
  justify-content: space-between;
  gap: 16px;
}

.details-header {
  display: grid;
  gap: 6px;
}

.provider {
  margin: 0;
  font-size: 0.85rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: #9c5a11;
}

.route {
  margin: 0;
  font-size: 1.25rem;
  font-weight: 700;
}

.fare-stack {
  width: 100%;
  margin-top: 18px;
}

.price-block {
  display: grid;
  justify-items: end;
  gap: 4px;
}

.price-block strong {
  font-size: 1.35rem;
}

.primary-fare-link {
  color: #1f5fbf;
  font-weight: 700;
  text-decoration: none;
}

.fare-summary {
  align-items: center;
  padding: 12px 14px;
  border-radius: 16px;
  background: #fff6e9;
  color: #6d4a20;
}

.fare-provider {
  display: grid;
  gap: 4px;
}

.fare-provider-label {
  font-weight: 700;
}

.expand-button {
  border: none;
  background: transparent;
  color: #1f5fbf;
  font-weight: 700;
  cursor: pointer;
}

.attached-expand {
  display: flex;
  justify-content: center;
  width: 100%;
  margin-top: 8px;
  padding: 10px 14px;
  border-radius: 0 0 14px 14px;
  background: #f2e7d1;
}

.price-block span,
.leg-summary span,
.segment-item span {
  color: #5f6973;
}

.leg-block {
  margin-top: 16px;
  padding: 16px;
  border-radius: 20px;
  background: #f6f8fb;
}

.leg-summary {
  align-items: center;
}

.leg-summary p {
  margin: 0 0 4px;
  font-weight: 700;
}

.segment-list {
  list-style: none;
  padding: 0;
  margin: 16px 0 0;
  display: grid;
  gap: 12px;
}

.segment-item {
  padding-top: 12px;
  border-top: 1px solid rgba(29, 34, 40, 0.08);
  font-size: 0.96rem;
}

.segment-item:first-child {
  border-top: none;
  padding-top: 0;
}

.other-fares {
  margin-top: 0;
  padding: 16px;
  border-radius: 0 20px 20px 20px;
  background: #f6efe3;
  border-top: 1px solid rgba(29, 34, 40, 0.08);
}

.other-fares-title {
  margin: 0 0 12px;
  font-weight: 700;
}

.other-fares-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: grid;
  gap: 12px;
}

.other-fare-item {
  align-items: center;
  padding-top: 12px;
  border-top: 1px solid rgba(29, 34, 40, 0.08);
}

.other-fare-item:first-child {
  padding-top: 0;
  border-top: none;
}

.other-fare-item div {
  display: grid;
  gap: 4px;
}

.other-fare-item a {
  color: #1f5fbf;
  font-weight: 700;
  text-decoration: none;
}

@media (max-width: 1100px) {
  .results-grid {
    grid-template-columns: 1fr;
  }

  .filters-panel {
    position: static;
  }
}

@media (max-width: 860px) {
  .airport-grid,
  .settings-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 680px) {
  .search-page {
    padding: 24px 14px 42px;
  }

  .search-shell-header,
  .results-header,
  .leg-summary,
  .segment-item,
  .fare-summary,
  .other-fare-item {
    flex-direction: column;
    align-items: start;
  }

  .price-block {
    justify-items: start;
  }
}
</style>
