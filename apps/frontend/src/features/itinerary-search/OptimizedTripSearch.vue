<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import AirportGroupPicker from '../../components/flight-search/AirportGroupPicker.vue'
import { useAirportPicker } from '../flight-search/useAirportPicker'
import { cancelItinerarySearch, getItinerarySearch, getItinerarySearchCapabilities, startItinerarySearch } from './api'
import ItineraryFiltersPanel from './ItineraryFiltersPanel.vue'
import ItineraryResultCard from './ItineraryResultCard.vue'
import OptimizedDestinationEditor, { type OptimizedDestinationModel } from './OptimizedDestinationEditor.vue'
import type { AirportOption } from '../flight-search/types'
import { useAuth } from '../auth/useAuth'
import type { ItineraryResult, ItineraryResultsQuery, ItinerarySearchSession, OptimizedTripRequest, Ranking } from './types'
import { trackItineraryEvent } from './analytics'

let sequence = 1
const isoDate = (offset: number) => { const date = new Date(); date.setDate(date.getDate() + offset); return date.toISOString().slice(0, 10) }
const createDestination = (): OptimizedDestinationModel => { const number = sequence++; return { id: `optimized-destination-${number}`, label: 'Unordered destination', airports: [], stayMode: 'minimumNights', nights: 2, continuity: 'inherit' } }
const startPicker = useAirportPicker([])
const fixedEndPicker = useAirportPicker([])
const airportGroupName = (airports: AirportOption[], fallback: string) => airports.length
  ? airports.map(airport => airport.name ? `${airport.name} (${airport.code})` : airport.code).join(' / ').slice(0, 80)
  : fallback
const startGroupLabel = computed(() => airportGroupName(startPicker.airports.value, 'Starting point'))
const fixedEndGroupLabel = computed(() => airportGroupName(fixedEndPicker.airports.value, 'Final stop'))
const destinations = ref<OptimizedDestinationModel[]>([createDestination()])
const preserveDestinationOrder = ref(true)
const endpointMode = ref<'returnToStart' | 'openEnded' | 'fixedEnd'>('returnToStart')
const startDate = ref(isoDate(1))
const endDate = ref(isoDate(8))
const defaultContinuity = ref<'sameAirport' | 'allowSwitch'>('sameAirport')
const adults = ref(1)
const cabinClass = ref('economy')
const ranking = ref<Ranking>('recommended')
const { user } = useAuth()
const session = ref<ItinerarySearchSession | null>(null)
const results = ref<ItineraryResult[]>([])
const query = ref<ItineraryResultsQuery>({ ranking: 'recommended', pageSize: 10, allowAirportSwitches: true })
const rankingLeaders = ref<Record<Ranking, ItineraryResult | null>>({ recommended: null, cheapest: null, fastest: null })
const configuredProviderCallBudget = ref<number | null>(null)
const maxOptimizedDestinations = ref(5)
const maxAirportsPerGroup = ref(5)
const maxTripDays = ref(31)
const error = ref('')
const pollError = ref('')
const submitting = ref(false)
const canceling = ref(false)
const loadingMore = ref(false)
const loadedPage = ref(1)
const formDirty = ref(false)
const searchStarted = ref(false)
const reportedSessions = new Set<string>()
const loadMoreSentinel = ref<HTMLElement | null>(null)
let timer: number | undefined
let controller: AbortController | null = null
let leaderController: AbortController | null = null
let observer: IntersectionObserver | null = null
let requestVersion = 0
let skipNextQueryRefresh = false

const rankingOptions: { value: Ranking; label: string }[] = [
  { value: 'recommended', label: 'Recommended' },
  { value: 'cheapest', label: 'Cheapest' },
  { value: 'fastest', label: 'Fastest' },
]
const phaseLabels: Record<string, string> = {
  validating: 'Validating trip constraints',
  searchingEdges: 'Searching flight edges',
  buildingItineraries: 'Building complete itineraries',
  finalizingRankings: 'Finalizing rankings',
  completed: 'Search complete',
  timeout: 'Search stopped at its time limit',
  canceled: 'Search canceled',
  failed: 'Search failed',
}

const dateNumber = (value: string) => {
  const [year, month, day] = value.split('-').map(Number)
  return year && month && day ? Math.floor(Date.UTC(year, month - 1, day) / 86_400_000) : null
}
const maxEndDate = computed(() => {
  const start = dateNumber(startDate.value)
  return start === null ? undefined : new Date((start + maxTripDays.value - 1) * 86_400_000).toISOString().slice(0, 10)
})
const gap = (destination: OptimizedDestinationModel) => Math.max(1, destination.nights)
const minimumCalendarDays = computed(() => {
  if (endpointMode.value !== 'openEnded') {
    const offset = destinations.value.reduce((total, destination) => total + gap(destination), 0)
    return Number.isFinite(offset) ? offset + 1 : Number.POSITIVE_INFINITY
  }
  if (preserveDestinationOrder.value) {
    const final = destinations.value[destinations.value.length - 1]
    const preceding = destinations.value.slice(0, -1).reduce((total, destination) => total + gap(destination), 0)
    return preceding + final.nights + 1
  }
  const candidates = destinations.value.map((final, finalIndex) => {
    const preceding = destinations.value.filter((_, index) => index !== finalIndex).reduce((total, destination) => total + gap(destination), 0)
    return preceding + final.nights + 1
  })
  return Math.min(...candidates)
})
const availableCalendarDays = computed(() => {
  const start = dateNumber(startDate.value); const end = dateNumber(endDate.value)
  return start === null || end === null ? 0 : end - start + 1
})
const requiredLegCount = computed(() => destinations.value.length + (endpointMode.value === 'openEnded' ? 0 : 1))
const factorial = (value: number): number => value <= 1 ? 1 : value * factorial(value - 1)
const airportChoiceEstimate = computed(() => {
  const startChoices = Math.max(1, startPicker.airports.value.length)
  const destinationChoices = destinations.value.reduce((total, destination) => total * Math.max(1, destination.airports.length), 1)
  const endChoices = endpointMode.value === 'fixedEnd' ? Math.max(1, fixedEndPicker.airports.value.length) : startChoices
  const routeOrderChoices = preserveDestinationOrder.value ? 1 : factorial(destinations.value.length)
  return routeOrderChoices * requiredLegCount.value * startChoices * destinationChoices * endChoices
})
const exceedsTripLimit = computed(() => availableCalendarDays.value > maxTripDays.value)
const impossible = computed(() => availableCalendarDays.value < minimumCalendarDays.value || exceedsTripLimit.value)
const feasibilityMessage = computed(() => exceedsTripLimit.value
  ? `This trip spans ${availableCalendarDays.value} calendar days, but the current limit is ${maxTripDays.value}.`
  : impossible.value
    ? `This date range has ${availableCalendarDays.value} calendar days but needs at least ${minimumCalendarDays.value}.`
    : '')
const providerCallBudget = computed(() => configuredProviderCallBudget.value ?? (user.value.roles.some(role => role.toLowerCase() === 'admin')
  ? 250
  : user.value.isAuthenticated ? 100 : 25))
const coverageExpectation = computed(() => airportChoiceEstimate.value > providerCallBudget.value
  ? `Likely bounded at the current ${providerCallBudget.value}-call allowance`
  : `Expected exhaustive within the current ${providerCallBudget.value}-call allowance`)
const isRunning = computed(() => session.value?.status === 'running')
const hasMore = computed(() => loadedPage.value < (session.value?.pagination?.totalPages ?? 0))
const phaseLabel = computed(() => phaseLabels[session.value?.phase ?? ''] ?? session.value?.phase ?? 'Preparing search')
const totalResults = computed(() => session.value?.pagination?.totalResults ?? results.value.length)
const duration = (minutes: number) => `${Math.floor(minutes / 60)}h ${minutes % 60}m`

const updateDestination = (index: number, destination: OptimizedDestinationModel) => { destinations.value[index] = destination }
const addDestination = () => { if (destinations.value.length < maxOptimizedDestinations.value) destinations.value = [...destinations.value, createDestination()] }
const removeDestination = (index: number) => { destinations.value = destinations.value.filter((_, position) => position !== index) }
const group = (id: string, label: string, airports: AirportOption[]) => ({ id, label: label.trim(), airportCodes: airports.map(airport => airport.code) })

const clearTimer = () => { if (timer) window.clearTimeout(timer); timer = undefined }
const setUrlSearchId = (searchId?: string) => {
  const url = new URL(window.location.href)
  if (searchId) url.searchParams.set('searchId', searchId)
  else url.searchParams.delete('searchId')
  window.history.replaceState(window.history.state, '', url)
}
const mergeResults = (incoming: ItineraryResult[], append: boolean) => {
  results.value = append
    ? [...results.value, ...incoming].filter((result, index, all) => all.findIndex(candidate => candidate.id === result.id) === index)
    : incoming
}
const baseFilterQuery = () => {
  const { ranking: _ranking, page: _page, pageSize: _pageSize, ...filters } = query.value
  return filters
}
const loadRankingLeaders = async (searchId: string, version: number) => {
  leaderController?.abort(); leaderController = new AbortController()
  try {
    const sessions = await Promise.all(rankingOptions.map(option => getItinerarySearch(searchId, { ...baseFilterQuery(), ranking: option.value, page: 1, pageSize: 1 }, leaderController!.signal)))
    if (version !== requestVersion || session.value?.searchId !== searchId) return
    rankingLeaders.value = {
      recommended: sessions[0].results[0] ?? null,
      cheapest: sessions[1].results[0] ?? null,
      fastest: sessions[2].results[0] ?? null,
    }
  } catch (reason) {
    if (!(reason instanceof Error && reason.name === 'AbortError')) rankingLeaders.value = { recommended: null, cheapest: null, fastest: null }
  }
}
const applySession = (next: ItinerarySearchSession, append = false) => {
  session.value = next
  loadedPage.value = next.pagination?.page ?? (append ? loadedPage.value : 1)
  mergeResults(next.results, append)
  if (next.status !== 'running' && !reportedSessions.has(next.searchId)) {
    reportedSessions.add(next.searchId)
    trackItineraryEvent('completed_search', { mode: 'optimize', status: next.status, coverage: next.coverage.mode, result_count: next.pagination?.totalResults ?? next.results.length })
    if (next.coverage.mode === 'bounded') trackItineraryEvent('bounded_coverage', { mode: 'optimize', provider_call_limit: next.coverage.providerCallLimit, live_provider_calls: next.coverage.liveProviderCallsUsed })
  }
}
const refresh = async (poll = true) => {
  const searchId = session.value?.searchId
  if (!searchId) return
  const version = requestVersion
  clearTimer()
  controller?.abort(); controller = new AbortController()
  try {
    const next = await getItinerarySearch(searchId, { ...query.value, page: 1 }, controller.signal)
    if (version !== requestVersion || session.value?.searchId !== searchId) return
    pollError.value = ''
    applySession(next)
    const resultCount = next.pagination?.totalResults ?? next.results.length
    if (resultCount > 0) void loadRankingLeaders(searchId, version)
    if (poll && next.status === 'running') timer = window.setTimeout(() => refresh(true), 500)
  } catch (reason) {
    if (version === requestVersion && !(reason instanceof Error && reason.name === 'AbortError')) pollError.value = reason instanceof Error ? reason.message : 'Could not load this search.'
  }
}

const resume = async (searchId: string) => {
  const version = ++requestVersion
  controller?.abort(); controller = new AbortController()
  try {
    const next = await getItinerarySearch(searchId, query.value, controller.signal)
    if (version !== requestVersion) return
    applySession(next)
    if (next.results.length) void loadRankingLeaders(searchId, version)
    if (next.status === 'running') timer = window.setTimeout(() => refresh(true), 300)
  } catch (reason) {
    if (version === requestVersion && !(reason instanceof Error && reason.name === 'AbortError')) {
      pollError.value = reason instanceof Error ? reason.message : 'This search session is no longer available.'
      setUrlSearchId()
    }
  }
}

const submit = async () => {
  error.value = ''
  if (startPicker.airports.value.length === 0 || destinations.value.some(destination => destination.airports.length === 0)) { error.value = 'Add at least one airport to the start and every destination.'; trackItineraryEvent('validation_failure', { mode: 'optimize', stage: 'airport_groups' }); return }
  if (endpointMode.value === 'fixedEnd' && fixedEndPicker.airports.value.length === 0) { error.value = 'Add at least one airport to the fixed ending group.'; trackItineraryEvent('validation_failure', { mode: 'optimize', stage: 'fixed_end' }); return }
  if (impossible.value) { error.value = feasibilityMessage.value; trackItineraryEvent('validation_failure', { mode: 'optimize', stage: 'date_window' }); return }
  const request: OptimizedTripRequest = {
    mode: 'optimize',
    start: group('optimized-start', startGroupLabel.value, startPicker.airports.value),
    destinations: destinations.value.map(destination => ({ group: group(destination.id, destination.label, destination.airports), stay: { mode: destination.stayMode, nights: destination.nights }, airportContinuity: destination.continuity })),
    endpointMode: endpointMode.value,
    fixedEnd: endpointMode.value === 'fixedEnd' ? group('optimized-fixed-end', fixedEndGroupLabel.value, fixedEndPicker.airports.value) : null,
    startDate: startDate.value,
    endDate: endDate.value,
    defaultAirportContinuity: defaultContinuity.value,
    preserveDestinationOrder: preserveDestinationOrder.value,
    adults: adults.value,
    cabinClass: cabinClass.value,
    ranking: ranking.value,
  }
  const previousSearchId = isRunning.value ? session.value?.searchId : undefined
  const version = ++requestVersion
  clearTimer()
  controller?.abort(); leaderController?.abort()
  if (previousSearchId) await cancelItinerarySearch(previousSearchId).catch(() => undefined)
  submitting.value = true
  pollError.value = ''
  results.value = []
  rankingLeaders.value = { recommended: null, cheapest: null, fastest: null }
  loadedPage.value = 1
  controller = new AbortController()
  try {
    const next = await startItinerarySearch(request, controller.signal)
    if (version !== requestVersion) return
    skipNextQueryRefresh = true
    query.value = { ...query.value, ranking: ranking.value, page: 1 }
    applySession(next)
    searchStarted.value = true
    setUrlSearchId(next.searchId)
    if (next.results.length) void loadRankingLeaders(next.searchId, version)
    if (next.status === 'running') timer = window.setTimeout(() => refresh(true), 50)
  } catch (reason) {
    if (version === requestVersion && !(reason instanceof Error && reason.name === 'AbortError')) { error.value = reason instanceof Error ? reason.message : 'Could not validate this trip.'; trackItineraryEvent('validation_failure', { mode: 'optimize', stage: 'server' }) }
  } finally { if (version === requestVersion) submitting.value = false }
}

const cancel = async () => {
  if (!session.value?.searchId || !isRunning.value) return
  const searchId = session.value.searchId
  canceling.value = true
  clearTimer(); controller?.abort(); leaderController?.abort()
  try {
    await cancelItinerarySearch(searchId)
    if (session.value?.searchId === searchId) session.value = { ...session.value, status: 'canceled', phase: 'canceled' }
  } catch (reason) {
    pollError.value = reason instanceof Error ? reason.message : 'Could not cancel this search.'
  } finally { canceling.value = false }
}

const loadMore = async () => {
  if (!session.value?.searchId || !hasMore.value || loadingMore.value || isRunning.value) return
  const version = requestVersion
  const searchId = session.value.searchId
  loadingMore.value = true
  try {
    const next = await getItinerarySearch(searchId, { ...query.value, page: loadedPage.value + 1 }, undefined)
    if (version === requestVersion && session.value?.searchId === searchId) applySession(next, true)
  } catch (reason) {
    pollError.value = reason instanceof Error ? reason.message : 'Could not load more itineraries.'
  } finally { loadingMore.value = false }
}

const selectRanking = (value: Ranking) => { trackItineraryEvent('result_selection', { ranking: value }); query.value = { ...query.value, ranking: value, page: 1 } }
const moveRanking = (current: Ranking, direction: number) => {
  const index = rankingOptions.findIndex(option => option.value === current)
  const next = rankingOptions[(index + direction + rankingOptions.length) % rankingOptions.length]
  selectRanking(next.value)
  window.setTimeout(() => document.getElementById(`ranking-tab-${next.value}`)?.focus())
}
const observeSentinel = (element: HTMLElement | null) => {
  observer?.disconnect()
  if (!element || typeof IntersectionObserver === 'undefined') return
  observer = new IntersectionObserver(entries => { if (entries.some(entry => entry.isIntersecting)) void loadMore() }, { rootMargin: '240px' })
  observer.observe(element)
}

watch(query, () => {
  if (skipNextQueryRefresh) { skipNextQueryRefresh = false; return }
  if (!session.value?.searchId) return
  loadedPage.value = 1
  void refresh(true)
}, { deep: true })
watch(loadMoreSentinel, observeSentinel)
onBeforeUnmount(() => { if (formDirty.value && !searchStarted.value) trackItineraryEvent('form_abandonment', { mode: 'optimize' }); clearTimer(); controller?.abort(); leaderController?.abort(); observer?.disconnect() })
onMounted(async () => {
  try {
    const capabilities = await getItinerarySearchCapabilities()
    configuredProviderCallBudget.value = capabilities.providerCallLimit
    if (capabilities.maxOptimizedDestinations > 0) maxOptimizedDestinations.value = capabilities.maxOptimizedDestinations
    if (capabilities.maxAirportsPerGroup > 0) maxAirportsPerGroup.value = capabilities.maxAirportsPerGroup
    if (capabilities.maxTripDays > 0) maxTripDays.value = capabilities.maxTripDays
  }
  catch { /* The role-based defaults keep preliminary feedback available while the feature is disabled. */ }
  const existingSearchId = new URL(window.location.href).searchParams.get('searchId')
  if (existingSearchId) await resume(existingSearchId)
})
</script>

<template>
  <section aria-label="Optimize my trip form">
    <form class="optimized-form" @input="formDirty = true" @submit.prevent="submit">
      <div class="mode-introduction"><strong>Optimize dates, airports, and flights</strong><p>{{ preserveDestinationOrder ? 'Destinations are visited in the order shown below. Aveon still finds the best dates and airport combinations for your stay rules.' : 'Destination cards are unordered. Aveon compares possible visit orders, dates, airport combinations, and stay schedules.' }}</p></div>
      <AirportGroupPicker v-model:input="startPicker.input.value" v-model:airports="startPicker.airports.value" label="Starting airport group" input-aria-label="Add a starting airport or city" suggestions-aria-label="Starting airport suggestions" suggestion-id-prefix="optimized-start" :suggestions="startPicker.suggestions.value" :suggestions-loading="startPicker.suggestionsLoading.value" :suggestions-error="startPicker.suggestionsError.value" :has-searched-suggestions="startPicker.hasSearchedSuggestions.value" :max-airports="maxAirportsPerGroup" @add-airport="startPicker.addAirport" @remove-airport="startPicker.removeAirport" @confirm-input="startPicker.confirmInput" />
      <div class="endpoint-dates">
        <label>Finish trip
          <select v-model="endpointMode" aria-label="Trip endpoint mode">
            <option value="returnToStart">Return to start</option><option value="openEnded">Finish at the last destination</option><option value="fixedEnd">Finish at a different airport group</option>
          </select>
        </label>
        <label>Start date<input v-model="startDate" type="date" required /></label>
        <label>End date<input v-model="endDate" type="date" :min="startDate" :max="maxEndDate" required /></label>
      </div>
      <AirportGroupPicker v-if="endpointMode === 'fixedEnd'" v-model:input="fixedEndPicker.input.value" v-model:airports="fixedEndPicker.airports.value" label="Fixed ending airport group" input-aria-label="Add a fixed ending airport or city" suggestions-aria-label="Fixed ending airport suggestions" suggestion-id-prefix="optimized-fixed-end" :suggestions="fixedEndPicker.suggestions.value" :suggestions-loading="fixedEndPicker.suggestionsLoading.value" :suggestions-error="fixedEndPicker.suggestionsError.value" :has-searched-suggestions="fixedEndPicker.hasSearchedSuggestions.value" :max-airports="maxAirportsPerGroup" @add-airport="fixedEndPicker.addAirport" @remove-airport="fixedEndPicker.removeAirport" @confirm-input="fixedEndPicker.confirmInput" />
      <label class="order-choice"><input v-model="preserveDestinationOrder" type="checkbox" /><span><strong>Keep destinations in the order shown</strong><small>Untick this to let Aveon rearrange them while looking for a better trip.</small></span></label>
      <OptimizedDestinationEditor v-for="(destination, index) in destinations" :key="destination.id" :model-value="destination" :index="index" :removable="destinations.length > 1" :max-airports="maxAirportsPerGroup" :preserve-order="preserveDestinationOrder" @update:model-value="updateDestination(index, $event)" @remove="removeDestination(index)" />
      <button type="button" class="secondary-action" :disabled="destinations.length >= maxOptimizedDestinations" @click="addDestination">{{ preserveDestinationOrder ? 'Add next destination' : 'Add another unordered destination' }}</button>
      <div class="trip-options">
        <label>Airport continuity<select v-model="defaultContinuity"><option value="sameAirport">Use the same airport</option><option value="allowSwitch">Allow airport changes</option></select></label>
        <label>Travellers<input v-model.number="adults" type="number" min="1" max="9" /></label>
        <label>Cabin<select v-model="cabinClass"><option value="economy">Economy</option><option value="premium_economy">Premium economy</option><option value="business">Business</option><option value="first">First</option></select></label>
        <label>Ranking<select v-model="ranking"><option value="recommended">Recommended</option><option value="cheapest">Cheapest</option><option value="fastest">Fastest</option></select></label>
      </div>
      <aside class="feasibility" :class="{ 'feasibility--invalid': impossible }" aria-live="polite">
        <strong>Trip feasibility</strong>
        <span>{{ requiredLegCount }} inter-city legs</span><span>{{ Number.isFinite(minimumCalendarDays) ? minimumCalendarDays : 'No' }} minimum calendar days</span><span>~{{ airportChoiceEstimate }} airport-route checks</span><span>{{ coverageExpectation }}</span>
        <p v-if="feasibilityMessage" role="alert">{{ feasibilityMessage }}</p>
      </aside>
      <p v-if="error" role="alert" class="form-error">{{ error }}</p>
      <button class="primary-action" type="submit" :disabled="submitting || impossible">{{ submitting ? 'Checking…' : 'Check and optimize trip' }}</button>
    </form>
    <section v-if="session?.feasibility" class="authoritative-feasibility" aria-live="polite">
      <strong>Schedule validation complete</strong>
      <p>{{ session.feasibility.generatedScheduleCount }} valid abstract schedules across {{ session.feasibility.routeOrderCount }} route orders. Coverage is {{ session.feasibility.bounded ? 'bounded' : 'exhaustive' }}.</p>
    </section>
    <section v-if="session" class="search-progress" aria-labelledby="search-progress-heading" aria-live="polite">
      <div class="progress-heading">
        <div>
          <p class="eyebrow">{{ session.status === 'running' ? 'Search in progress' : 'Search status' }}</p>
          <div class="progress-title"><span v-if="isRunning" class="progress-spinner" aria-hidden="true" /><h2 id="search-progress-heading">{{ phaseLabel }}</h2></div>
        </div>
        <strong>{{ Math.round(session.progress) }}%</strong>
      </div>
      <progress :value="session.progress" max="100">{{ Math.round(session.progress) }}%</progress>
      <dl class="coverage-grid">
        <div><dt>Coverage</dt><dd>{{ session.coverage.mode === 'exhaustive' ? 'Exhaustive' : 'Best found within limits' }}</dd></div>
        <div><dt>Provider calls</dt><dd>{{ session.coverage.liveProviderCallsUsed }} / {{ session.coverage.providerCallLimit }}</dd></div>
        <div><dt>Cache hits</dt><dd>{{ session.coverage.cacheHits }}</dd></div>
        <div><dt>Candidates checked</dt><dd>{{ session.coverage.candidateStatesEvaluated }}</dd></div>
      </dl>
      <div v-if="session.warnings.length" class="session-warnings" role="status" aria-live="assertive">
        <strong>Important search notes</strong>
        <p v-for="warning in session.warnings" :key="warning.code">{{ warning.message }}</p>
      </div>
      <p v-if="pollError" class="form-error" role="alert">{{ pollError }}</p>
      <div class="progress-actions">
        <button v-if="isRunning" type="button" class="secondary-action" :disabled="canceling" @click="cancel">{{ canceling ? 'Canceling…' : 'Cancel search' }}</button>
        <button v-if="pollError || session.status === 'failed'" type="button" class="secondary-action" @click="refresh(true)">Retry loading search</button>
      </div>
    </section>

    <section v-if="session" class="results-section" aria-labelledby="optimized-results-heading">
      <div class="results-heading">
        <div><p class="eyebrow">Complete itineraries</p><h2 id="optimized-results-heading">{{ totalResults }} trip{{ totalResults === 1 ? '' : 's' }} found</h2></div>
        <span v-if="isRunning">More results may appear while we search.</span>
      </div>
      <div class="ranking-tabs" role="tablist" aria-label="Compare itinerary rankings">
        <button v-for="option in rankingOptions" :id="`ranking-tab-${option.value}`" :key="option.value" type="button" role="tab" aria-controls="optimized-results-list" :aria-selected="query.ranking === option.value" :tabindex="query.ranking === option.value ? 0 : -1" :class="{ active: query.ranking === option.value }" @click="selectRanking(option.value)" @keydown.left.prevent="moveRanking(option.value, -1)" @keydown.right.prevent="moveRanking(option.value, 1)">
          <strong>{{ option.label }}</strong>
          <span v-if="rankingLeaders[option.value]">{{ rankingLeaders[option.value]?.currency }} {{ rankingLeaders[option.value]?.totalPrice.toFixed(2) }} · {{ duration(rankingLeaders[option.value]!.totalFlightDurationMinutes) }}</span>
          <span v-else>Waiting for a complete trip</span>
        </button>
      </div>
      <div class="results-layout">
        <ItineraryFiltersPanel v-model="query" :filters="session.filters" />
        <div id="optimized-results-list" class="result-list" role="tabpanel" :aria-labelledby="`ranking-tab-${query.ranking ?? 'recommended'}`" aria-live="polite" :aria-busy="isRunning || loadingMore" tabindex="0">
          <ItineraryResultCard v-for="result in results" :key="result.id" :result="result" />
          <div v-if="results.length === 0 && isRunning" class="empty-state"><strong>Building complete itineraries…</strong><p>Results will appear here as soon as every flight in a trip is available.</p></div>
          <div v-else-if="results.length === 0 && session.status === 'canceled'" class="empty-state"><strong>Search canceled</strong><p>Start another search whenever you are ready.</p></div>
          <div v-else-if="results.length === 0 && session.status === 'failed'" class="empty-state"><strong>No itineraries could be loaded</strong><p>{{ session.errorMessage || 'Retry the search or adjust the trip constraints.' }}</p></div>
          <div v-else-if="results.length === 0" class="empty-state"><strong>No trips match these filters</strong><p>Clear one or more filters to see other complete itineraries.</p></div>
          <div v-if="hasMore && !isRunning" ref="loadMoreSentinel" class="load-more-sentinel">
            <button type="button" class="secondary-action" :disabled="loadingMore" @click="loadMore">{{ loadingMore ? 'Loading…' : 'Load more itineraries' }}</button>
          </div>
        </div>
      </div>
    </section>
  </section>
</template>

<style scoped>
.optimized-form { display: grid; gap: 16px; }
.mode-introduction { padding: 14px 16px; border-radius: 10px; background: var(--brand-soft); color: var(--ink-strong); }
.mode-introduction p { margin: 5px 0 0; color: var(--muted); }
.order-choice { display: flex; align-items: flex-start; gap: 10px; padding: 12px 14px; border: 1px solid var(--border); border-radius: 10px; background: var(--surface); cursor: pointer; }
.order-choice input { width: auto; margin-top: 3px; accent-color: var(--brand); }
.order-choice span { display: grid; gap: 3px; color: var(--ink-strong); }
.order-choice small { color: var(--muted); }
.endpoint-dates, .trip-options { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.trip-options { grid-template-columns: repeat(4, minmax(0, 1fr)); }
label { display: grid; gap: 5px; color: var(--muted); font-size: .9rem; }
input, select { width: 100%; box-sizing: border-box; padding: 9px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface); color: var(--ink-strong); }
.secondary-action, .primary-action { justify-self: start; padding: 10px 14px; border-radius: 8px; cursor: pointer; }
.secondary-action { border: 1px solid var(--border); background: var(--surface); color: var(--ink-strong); }.secondary-action:disabled { opacity: .5; }
.primary-action { border: 0; background: var(--brand); color: white; font-weight: 700; }.primary-action:disabled { opacity: .6; cursor: not-allowed; }
.feasibility { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 8px 16px; padding: 14px; border-radius: 10px; background: var(--brand-soft); color: var(--ink-strong); }
.feasibility strong, .feasibility p { grid-column: 1 / -1; }.feasibility span { font-size: .9rem; }.feasibility p { margin: 0; color: #b42318; }.feasibility--invalid { background: #fff1f0; }
.form-error { color: #b42318; }.authoritative-feasibility { margin-top: 18px; padding: 14px; border: 1px solid var(--border); border-radius: 10px; }.authoritative-feasibility p { margin-bottom: 0; }
.search-progress, .results-section { margin-top: 20px; }.search-progress { display: grid; gap: 14px; padding: 18px; border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--surface-raised); }.progress-heading, .results-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; }.progress-heading h2, .results-heading h2 { margin: 2px 0 0; }.eyebrow { margin: 0; color: var(--brand-strong); font-size: .78rem; font-weight: 800; letter-spacing: .08em; text-transform: uppercase; }.search-progress progress { width: 100%; height: 8px; overflow: hidden; appearance: none; border: 0; border-radius: 999px; background: #e8ecf4; pointer-events: none; }.search-progress progress::-webkit-progress-bar { border-radius: 999px; background: #e8ecf4; }.search-progress progress::-webkit-progress-value { border-radius: 999px; background: linear-gradient(90deg, var(--brand), var(--accent)); }.search-progress progress::-moz-progress-bar { border-radius: 999px; background: linear-gradient(90deg, var(--brand), var(--accent)); }.coverage-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 10px; margin: 0; }.coverage-grid div { padding: 10px; border-radius: 8px; background: var(--surface); }.coverage-grid dt { color: var(--muted); font-size: .78rem; }.coverage-grid dd { margin: 3px 0 0; font-weight: 700; }.session-warnings { padding: 12px; border-left: 4px solid #d98b00; border-radius: 8px; background: #fff8e8; color: #714500; }.session-warnings p { margin: 5px 0 0; }.progress-actions { display: flex; gap: 8px; }
.progress-title { display: flex; align-items: center; gap: 10px; }.progress-spinner { width: 15px; height: 15px; flex: 0 0 15px; border: 2px solid color-mix(in srgb, var(--brand) 22%, transparent); border-top-color: var(--brand); border-radius: 50%; animation: progress-spin .75s linear infinite; }@keyframes progress-spin { to { transform: rotate(360deg); } }
.results-section { display: grid; gap: 16px; }.results-heading span { color: var(--muted); }.ranking-tabs { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 8px; }.ranking-tabs button { display: grid; gap: 4px; min-width: 0; padding: 12px; border: 1px solid var(--border); border-radius: 9px; background: var(--surface); color: var(--ink-strong); text-align: left; cursor: pointer; }.ranking-tabs button.active { border-color: var(--brand); box-shadow: inset 0 0 0 1px var(--brand); background: var(--brand-soft); }.ranking-tabs span { overflow: hidden; color: var(--muted); font-size: .82rem; text-overflow: ellipsis; white-space: nowrap; }.results-layout { display: grid; grid-template-columns: minmax(220px, 280px) minmax(0, 1fr); gap: 16px; align-items: start; }.result-list { display: grid; gap: 14px; min-width: 0; }.empty-state { padding: 32px 20px; border: 1px dashed var(--border); border-radius: var(--radius-md); text-align: center; }.empty-state p { margin-bottom: 0; color: var(--muted); }.load-more-sentinel { display: flex; justify-content: center; min-height: 48px; }
@media (max-width: 800px) { .endpoint-dates, .trip-options, .feasibility, .coverage-grid { grid-template-columns: 1fr 1fr; }.results-layout { grid-template-columns: 1fr; }.results-layout :deep(.filters) { grid-template-columns: repeat(2, minmax(0, 1fr)); }.results-layout :deep(.filters h2) { grid-column: 1 / -1; } }
@media (max-width: 560px) { .endpoint-dates, .trip-options, .feasibility, .coverage-grid, .ranking-tabs, .results-layout :deep(.filters) { grid-template-columns: 1fr; }.progress-heading, .results-heading { flex-direction: column; }.ranking-tabs span { white-space: normal; } }
</style>
