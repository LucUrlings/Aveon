<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { getItinerarySearch, getItinerarySearchCapabilities, startItinerarySearch } from './api'
import ItineraryFiltersPanel from './ItineraryFiltersPanel.vue'
import ItineraryResultCard from './ItineraryResultCard.vue'
import OrderedLegEditor, { type OrderedLegModel } from './OrderedLegEditor.vue'
import type { ItineraryResultsQuery, ItinerarySearchSession, OrderedLegSearchStatus, OrderedTripRequest, Ranking } from './types'
import { trackItineraryEvent } from './analytics'
import { fetchAirportSuggestions } from '../flight-search/api'
import type { AirportOption } from '../flight-search/types'

const props = withDefaults(defineProps<{
  prefillRoute?: string[]
  prefillDepartureDate?: string
  exploreHandoff?: boolean
}>(), {
  prefillRoute: () => [],
  prefillDepartureDate: '',
  exploreHandoff: false,
})

let sequence = 1
const nextDate = () => {
  const date = new Date(); date.setDate(date.getDate() + sequence)
  return date.toISOString().slice(0, 10)
}
const createLeg = (): OrderedLegModel => { const number = sequence++; return { id: `ordered-leg-${number}`, fromLabel: 'Starting airport group', toLabel: `Destination ${number}`, from: [], to: [], departureDate: nextDate(), continuity: 'sameAirport' } }
const legs = ref<OrderedLegModel[]>([createLeg()])
const returnToStart = ref(false)
const returnDate = ref('')
const adults = ref(1)
const cabinClass = ref('economy')
const ranking = ref<Ranking>('recommended')
const session = ref<ItinerarySearchSession | null>(null)
const query = ref<ItineraryResultsQuery>({ ranking: 'recommended', pageSize: 25, allowAirportSwitches: true })
const error = ref('')
const submitting = ref(false)
const maxOrderedLegs = ref(8)
const maxAirportsPerGroup = ref(5)
const formDirty = ref(false)
const searchStarted = ref(false)
const prefillActive = ref(false)
const lastHydratedPrefill = ref('')
const reportedSessions = new Set<string>()
let timer: number | undefined
let controller: AbortController | null = null
let prefillHydration = 0
const isRunning = computed(() => session.value?.status === 'running')
const lastDepartureDate = computed(() => legs.value.at(-1)?.departureDate ?? '')
const addDays = (value: string, days: number) => {
  const date = new Date(`${value}T12:00:00`)
  if (Number.isNaN(date.getTime())) return ''
  date.setDate(date.getDate() + days)
  return date.toISOString().slice(0, 10)
}
const generatedReturnLeg = computed<OrderedLegModel | null>(() => {
  if (!returnToStart.value) return null
  const first = legs.value[0]
  const last = legs.value.at(-1)
  if (!first || !last) return null
  return {
    id: 'ordered-return-to-start',
    fromLabel: last.toLabel,
    toLabel: `${first.fromLabel} (return home)`,
    from: [...last.to],
    to: [...first.from],
    departureDate: returnDate.value,
    continuity: 'sameAirport',
  }
})
const submittedLegs = computed(() => generatedReturnLeg.value ? [...legs.value, generatedReturnLeg.value] : legs.value)
const maxEditableLegs = computed(() => Math.max(1, maxOrderedLegs.value - (returnToStart.value ? 1 : 0)))
const routeSummary = computed<OrderedLegSearchStatus[]>(() => session.value?.orderedLegs ?? submittedLegs.value.map(leg => ({
  legId: leg.id,
  fromLabel: leg.fromLabel,
  toLabel: leg.toLabel,
  fromAirportCodes: leg.from.map(airport => airport.code),
  toAirportCodes: leg.to.map(airport => airport.code),
  departureDate: leg.departureDate,
  status: session.value?.status === 'running' ? 'searching' : 'pending',
  airportPairsPlanned: leg.from.length * leg.to.length,
  airportPairsScheduled: 0,
  airportPairsCompleted: 0,
  faresFound: 0,
  failedPairs: 0,
})))
const everyLegHasFares = computed(() => routeSummary.value.length > 0 && routeSummary.value.every(leg => leg.faresFound > 0))
const noCompleteItinerary = computed(() => session.value?.warnings.some(warning => warning.code === 'noCompleteItinerary') ?? false)
const orderedPhaseLabel = computed(() => session.value?.status === 'running' ? 'Searching your route' : session.value?.status === 'failed' ? 'Route search failed' : 'Route search complete')
const airportCodes = (values: string[]) => values.join(' / ')
const airportNames = (airports: AirportOption[]) => airports.map(airport => airport.displayLabel || airport.name || airport.code).join(' / ')
const legStatusLabel = (leg: OrderedLegSearchStatus) => {
  if (leg.status === 'faresFound') return `${leg.faresFound} fare${leg.faresFound === 1 ? '' : 's'} found`
  if (leg.status === 'noFares') return 'No fares found'
  if (leg.status === 'failed') return 'Airport-pair searches failed'
  if (leg.status === 'limited') return leg.airportPairsCompleted > 0 ? 'No fares in checked pairs' : 'Not searched within call limit'
  if (leg.status === 'searching') return `Searching ${leg.airportPairsCompleted} / ${leg.airportPairsScheduled || leg.airportPairsPlanned} airport pairs`
  return 'Waiting to search'
}
const recordSessionAnalytics = (next: ItinerarySearchSession) => {
  if (next.status === 'running' || reportedSessions.has(next.searchId)) return
  reportedSessions.add(next.searchId)
  trackItineraryEvent('completed_search', { mode: 'ordered', status: next.status, coverage: next.coverage.mode, result_count: next.pagination?.totalResults ?? next.results.length })
  if (next.coverage.mode === 'bounded') trackItineraryEvent('bounded_coverage', { mode: 'ordered', provider_call_limit: next.coverage.providerCallLimit, live_provider_calls: next.coverage.liveProviderCallsUsed })
}

const reconnectRoute = (route: OrderedLegModel[]) => route.map((leg, index) => index === 0 ? leg : ({ ...leg, from: [...route[index - 1].to], fromLabel: route[index - 1].toLabel }))
const removePrefillFromUrl = () => {
  if (!prefillActive.value) return
  prefillActive.value = false
  try {
    const url = new URL(window.location.href)
    url.searchParams.delete('prefill')
    url.searchParams.delete('route')
    url.searchParams.delete('departureDate')
    url.searchParams.delete('source')
    window.history.replaceState(window.history.state, '', `${url.pathname}${url.search}${url.hash}`)
  } catch { /* Unit-test and embedded URLs may not support history replacement. */ }
}
const markFormDirty = () => { formDirty.value = true; removePrefillFromUrl() }
const updateLeg = (index: number, leg: OrderedLegModel) => {
  markFormDirty()
  const route = [...legs.value]
  route[index] = leg
  legs.value = reconnectRoute(route)
}
const addLeg = () => {
  markFormDirty()
  if (legs.value.length >= maxEditableLegs.value) return
  legs.value = reconnectRoute([...legs.value, createLeg()])
}
const removeLeg = (index: number) => {
  markFormDirty()
  const route = [...legs.value]
  if (index === 0 && route[1]) route[1] = { ...route[1], from: [...route[0].from], fromLabel: route[0].fromLabel }
  legs.value = reconnectRoute(route.filter((_, position) => position !== index))
}
const group = (id: string, label: string, airports: OrderedLegModel['from']) => ({ id, label: label.trim(), airportCodes: airports.map(airport => airport.code) })
const toggleReturn = () => {
  markFormDirty()
  if (returnToStart.value && !returnDate.value) returnDate.value = addDays(lastDepartureDate.value, 1)
}

const refresh = async (poll = false) => {
  if (!session.value?.searchId) return
  controller?.abort(); controller = new AbortController()
  try {
    session.value = await getItinerarySearch(session.value.searchId, query.value, controller.signal)
    recordSessionAnalytics(session.value)
    if (poll && session.value.status === 'running') timer = window.setTimeout(() => refresh(true), 700)
  } catch (reason) {
    if (!(reason instanceof Error && reason.name === 'AbortError')) error.value = reason instanceof Error ? reason.message : 'Could not load results.'
  }
}

const submit = async () => {
  removePrefillFromUrl()
  error.value = ''
  if (legs.value.some(leg => !leg.departureDate || leg.from.length === 0 || leg.to.length === 0)) { error.value = 'Add a date and at least one airport to both ends of every flight.'; trackItineraryEvent('validation_failure', { mode: 'ordered', stage: 'route_fields' }); return }
  if (legs.value.some(leg => [...leg.from, ...leg.to].some(airport => !/^[A-Z]{3}$/.test(airport.code)))) { error.value = 'A four-letter airport identifier could not be resolved to its three-letter booking code. Select the airport from the suggestions and try again.'; trackItineraryEvent('validation_failure', { mode: 'ordered', stage: 'airport_identifier' }); return }
  if (returnToStart.value && (!returnDate.value || returnDate.value < lastDepartureDate.value)) { error.value = 'Choose a return date on or after the final outbound flight.'; trackItineraryEvent('validation_failure', { mode: 'ordered', stage: 'return_date' }); return }
  submitting.value = true
  controller?.abort(); controller = new AbortController()
  const request: OrderedTripRequest = {
    mode: 'ordered', adults: adults.value, cabinClass: cabinClass.value, ranking: ranking.value,
    legs: submittedLegs.value.map((leg, index) => ({ id: leg.id, from: group(`${leg.id}-from`, leg.fromLabel, leg.from), to: group(`${leg.id}-to`, leg.toLabel, leg.to), departureDate: leg.departureDate, airportContinuityWithPrevious: index === 0 ? 'sameAirport' : leg.continuity })),
  }
  try {
    session.value = await startItinerarySearch(request, controller.signal)
    searchStarted.value = true
    query.value = { ...query.value, ranking: ranking.value }
    timer = window.setTimeout(() => refresh(true), 100)
  } catch (reason) { error.value = reason instanceof Error ? reason.message : 'Could not start the search.'; trackItineraryEvent('validation_failure', { mode: 'ordered', stage: 'server' }) }
  finally { submitting.value = false }
}

watch(query, () => { if (session.value?.searchId) refresh(false) }, { deep: true })
watch(() => props.prefillRoute, codes => {
  const normalized = codes.map(code => code.trim().toUpperCase()).filter(code => /^[A-Z]{3,4}$/.test(code))
  const key = normalized.join(',')
  if (normalized.length < 2 || new Set(normalized).size !== normalized.length || key === lastHydratedPrefill.value) return
  const hydration = ++prefillHydration
  const allowedIdentifiers = normalized.slice(0, maxOrderedLegs.value + 1)
  const applyPrefill = (airports: AirportOption[]) => {
    const start = new Date(); start.setDate(start.getDate() + 1)
    legs.value = airports.slice(0, -1).map((airport, index) => {
      const departure = new Date(start); departure.setDate(start.getDate() + index)
      return {
        id: `ordered-prefill-${index + 1}`,
        fromLabel: airport.displayLabel,
        toLabel: airports[index + 1].displayLabel,
        from: [airport],
        to: [airports[index + 1]],
        departureDate: props.exploreHandoff
          ? index === 0 ? props.prefillDepartureDate : ''
          : departure.toISOString().slice(0, 10),
        continuity: 'sameAirport',
      }
    })
  }
  const placeholders = allowedIdentifiers.map(code => ({ code, name: null, displayLabel: code }))
  applyPrefill(placeholders)
  lastHydratedPrefill.value = key
  prefillActive.value = true
  formDirty.value = false
  void Promise.all(allowedIdentifiers.map(async identifier => {
    try {
      const matches = await fetchAirportSuggestions(identifier)
      return matches.find(airport => airport.code === identifier) ?? matches[0] ?? null
    } catch {
      return null
    }
  })).then(resolved => {
    if (hydration !== prefillHydration || searchStarted.value || resolved.some(airport => airport === null)) return
    const replacements = new Map(allowedIdentifiers.map((identifier, index) => [identifier, resolved[index] as AirportOption]))
    const resolveAirports = (airports: AirportOption[]) => airports.map(airport => replacements.get(airport.code) ?? airport)
    const resolveLabel = (label: string) => replacements.get(label)?.displayLabel ?? label
    legs.value = reconnectRoute(legs.value.map(leg => ({
      ...leg,
      from: resolveAirports(leg.from),
      to: resolveAirports(leg.to),
      fromLabel: resolveLabel(leg.fromLabel),
      toLabel: resolveLabel(leg.toLabel),
    })))
  })
}, { immediate: true, deep: true })
onBeforeUnmount(() => { if (formDirty.value && !searchStarted.value) trackItineraryEvent('form_abandonment', { mode: 'ordered' }); if (timer) window.clearTimeout(timer); controller?.abort() })
onMounted(async () => {
  try {
    const capabilities = await getItinerarySearchCapabilities()
    if (capabilities.maxOrderedLegs > 0) {
      maxOrderedLegs.value = capabilities.maxOrderedLegs
      if (legs.value.length > maxOrderedLegs.value) legs.value = reconnectRoute(legs.value.slice(0, maxOrderedLegs.value))
    }
    if (capabilities.maxAirportsPerGroup > 0) maxAirportsPerGroup.value = capabilities.maxAirportsPerGroup
  } catch { /* Backend validation remains authoritative if capabilities are temporarily unavailable. */ }
})
</script>

<template>
  <section aria-label="Build my route form">
    <form class="ordered-form" @input="markFormDirty" @submit.prevent="submit">
      <aside v-if="exploreHandoff" class="explore-handoff-note" role="note">
        <strong>Complete the dates for this explored route</strong>
        <p>Only the first leave date was checked in Explore. Choose dates for every later flight. An onward route may not operate or return fares on the dates you select here.</p>
      </aside>
      <div class="mode-introduction"><strong>Keep this exact route order</strong><p>Enter each stop once. Every new destination automatically continues from the one above it; Aveon searches the airport combinations and dates without rearranging your stops.</p></div>
      <OrderedLegEditor v-for="(leg, index) in legs" :key="`${leg.id}:${leg.from.map(airport => airport.code).join(',')}:${leg.to.map(airport => airport.code).join(',')}`" :model-value="leg" :index="index" :removable="legs.length > 1" :max-airports="maxAirportsPerGroup" @update:model-value="updateLeg(index, $event)" @remove="removeLeg(index)" />
      <button type="button" class="secondary-action add-destination-action" :disabled="legs.length >= maxEditableLegs" @click="addLeg">
        <span class="add-icon" aria-hidden="true">+</span>
        <span><strong>Add another flight to this route</strong><small>Continue from the current destination to one more place.</small></span>
      </button>
      <div class="return-to-start">
        <label class="return-choice">
          <input v-model="returnToStart" type="checkbox" :disabled="!returnToStart && legs.length >= maxOrderedLegs" @change="toggleReturn" />
          <span><strong>Return to starting point</strong><small>Add a final flight back to the starting airport group without entering it again.</small></span>
        </label>
        <div v-if="returnToStart" class="return-details">
          <label>Return date<input v-model="returnDate" type="date" :min="lastDepartureDate" required /></label>
          <div class="generated-return" aria-label="Generated return leg">
            <span>Return home</span>
            <strong>{{ airportNames(legs.at(-1)?.to ?? []) || 'Final destination' }} → {{ airportNames(legs[0]?.from ?? []) || 'Starting point' }}</strong>
            <small>This final leg is searched and reported separately, so you can see if its fares are missing or its provider calls fail.</small>
          </div>
        </div>
      </div>
      <div class="options-divider"><span>Trip and result options</span></div>
      <div class="search-options">
        <p>These settings apply to the complete route.</p>
        <div class="trip-options">
          <label>Travellers<input v-model.number="adults" type="number" min="1" max="9" /></label>
          <label>Cabin<select v-model="cabinClass"><option value="economy">Economy</option><option value="premium_economy">Premium economy</option><option value="business">Business</option><option value="first">First</option></select></label>
          <label>Ranking<select v-model="ranking"><option value="recommended">Recommended</option><option value="cheapest">Cheapest</option><option value="fastest">Fastest</option></select></label>
        </div>
      </div>
      <p v-if="error" role="alert" class="form-error">{{ error }}</p>
      <button class="primary-action" type="submit" :disabled="submitting">{{ submitting ? 'Starting…' : 'Search complete route' }}</button>
    </form>

    <section v-if="session" class="route-search-progress" aria-labelledby="ordered-search-progress-heading" aria-live="polite">
      <div class="progress-heading">
        <div><p class="eyebrow">{{ isRunning ? 'Search in progress' : 'Search status' }}</p><div class="progress-title"><span v-if="isRunning" class="progress-spinner" aria-hidden="true" /><h2 id="ordered-search-progress-heading">{{ orderedPhaseLabel }}</h2></div></div>
        <strong>{{ Math.round(session.progress) }}%</strong>
      </div>
      <progress :value="session.progress" max="100">{{ Math.round(session.progress) }}%</progress>
      <div class="route-summary-heading"><strong>Your route</strong><span>Each line shows whether that flight leg returned bookable fares.</span></div>
      <ol class="route-summary" aria-label="Route leg search status">
        <li v-for="(leg, index) in routeSummary" :key="leg.legId" :class="`leg-status leg-status--${leg.status}`">
          <span class="leg-number">{{ index + 1 }}</span>
          <div class="leg-route"><strong>{{ leg.fromLabel }} → {{ leg.toLabel }}</strong><span>{{ airportCodes(leg.fromAirportCodes) }} → {{ airportCodes(leg.toAirportCodes) }} · {{ leg.departureDate }}</span></div>
          <div class="leg-outcome"><strong>{{ legStatusLabel(leg) }}</strong><span v-if="leg.airportPairsScheduled > 0">{{ leg.airportPairsCompleted }} of {{ leg.airportPairsScheduled }} selected pairs checked<span v-if="leg.failedPairs > 0"> · {{ leg.failedPairs }} failed</span><span v-if="leg.airportPairsScheduled < leg.airportPairsPlanned"> · {{ leg.airportPairsPlanned }} possible</span></span></div>
        </li>
      </ol>
      <p v-if="session.status === 'completed' && noCompleteItinerary && everyLegHasFares" class="connection-note">Every leg returned fares, but none could be connected into a complete itinerary under the selected dates and airport-continuity rules.</p>
      <p v-else-if="session.status === 'completed' && noCompleteItinerary" class="connection-note connection-note--problem">No complete itinerary was found. Legs marked “No fares” or “Not searched” show where the route broke.</p>
      <p v-if="session.status === 'failed'" role="alert" class="form-error">{{ session.errorMessage ?? 'Search failed.' }}</p>
      <p v-for="warning in session.warnings" :key="warning.code" class="form-warning">{{ warning.message }}</p>
    </section>

    <div v-if="session && (session.results.length || session.status === 'completed')" class="results-layout">
      <ItineraryFiltersPanel v-model="query" :filters="session.filters" />
      <section aria-label="Ordered route results" class="results-list">
        <p>{{ session.pagination?.totalResults ?? session.results.length }} complete itineraries</p>
        <ItineraryResultCard v-for="result in session.results" :key="result.id" :result="result" />
        <p v-if="session.status === 'completed' && session.results.length === 0">No complete itinerary matches these route rules and filters.</p>
        <nav v-if="(session.pagination?.totalPages ?? 0) > 1" class="pagination" aria-label="Itinerary result pages">
          <button type="button" :disabled="(session.pagination?.page ?? 1) <= 1" @click="query = { ...query, page: (session.pagination?.page ?? 1) - 1 }">Previous</button>
          <span>Page {{ session.pagination?.page }} of {{ session.pagination?.totalPages }}</span>
          <button type="button" :disabled="(session.pagination?.page ?? 1) >= (session.pagination?.totalPages ?? 1)" @click="query = { ...query, page: (session.pagination?.page ?? 1) + 1 }">Next</button>
        </nav>
      </section>
    </div>
  </section>
</template>

<style scoped>
.ordered-form { display: grid; gap: 16px; }
.explore-handoff-note { padding: 13px 15px; border-left: 3px solid #d98b00; border-radius: 8px; background: #fff8e8; color: #714500; }.explore-handoff-note p { margin: 5px 0 0; line-height: 1.55; }
.mode-introduction { padding: 14px 16px; border-radius: 10px; background: var(--brand-soft); color: var(--ink-strong); }
.mode-introduction p { margin: 5px 0 0; color: var(--muted); }
.secondary-action, .primary-action { justify-self: start; padding: 10px 14px; border-radius: 8px; cursor: pointer; }
.secondary-action { border: 1px solid var(--border); background: var(--surface); color: var(--ink-strong); }
.add-destination-action { display: flex; width: 100%; box-sizing: border-box; align-items: center; gap: 12px; padding: 13px 15px; border: 2px dashed var(--border-strong); background: color-mix(in srgb, var(--brand-soft) 35%, var(--surface)); text-align: left; }.add-destination-action:hover:not(:disabled) { border-color: var(--brand); background: var(--brand-soft); }.add-destination-action > span:last-child { display: grid; gap: 2px; }.add-destination-action small { color: var(--muted); font-weight: 400; }.add-icon { display: grid; width: 30px; height: 30px; flex: 0 0 30px; place-items: center; border-radius: 50%; background: var(--brand); color: white; font-size: 1.35rem; line-height: 1; }.add-destination-action:disabled { opacity: .5; cursor: not-allowed; }
.return-to-start { display: grid; gap: 10px; }.return-choice { display: flex; align-items: flex-start; gap: 10px; padding: 12px 14px; border: 1px solid var(--border); border-radius: 10px; background: var(--surface); cursor: pointer; }.return-choice input { width: auto; margin-top: 3px; accent-color: var(--brand); }.return-choice span { display: grid; gap: 3px; }.return-choice small, .generated-return span, .generated-return small { color: var(--muted); }.return-details { display: grid; grid-template-columns: minmax(180px, 220px) minmax(0, 1fr); gap: 12px; align-items: stretch; padding-left: 24px; }.return-details label { display: grid; gap: 5px; color: var(--muted); }.return-details input { padding: 9px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface); color: var(--ink-strong); }.generated-return { display: grid; gap: 3px; padding: 10px 12px; border-left: 3px solid var(--brand); border-radius: 8px; background: var(--brand-soft); }
.primary-action { border: 0; background: var(--brand); color: white; font-weight: 700; }
.primary-action:disabled { opacity: .6; cursor: wait; }
.options-divider { display: flex; align-items: center; gap: 12px; margin-top: 8px; color: var(--muted); font-size: .76rem; font-weight: 800; letter-spacing: .08em; text-transform: uppercase; }.options-divider::before, .options-divider::after { content: ''; height: 1px; flex: 1; background: var(--border); }.search-options { display: grid; gap: 10px; }.search-options > p { margin: 0; color: var(--muted); font-size: .86rem; }.trip-options { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.trip-options label { display: grid; gap: 5px; color: var(--muted); }
.trip-options input, .trip-options select { padding: 9px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface); color: var(--ink-strong); }
.form-error { color: #b42318; }.form-warning { color: #9b5c00; }
.route-search-progress { display: grid; gap: 14px; margin-top: 20px; padding: 18px; border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--surface-raised); }
.progress-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; }.progress-heading h2 { margin: 2px 0 0; }.eyebrow { margin: 0; color: var(--brand-strong); font-size: .78rem; font-weight: 800; letter-spacing: .08em; text-transform: uppercase; }
.progress-title { display: flex; align-items: center; gap: 10px; }.progress-spinner { width: 15px; height: 15px; flex: 0 0 15px; border: 2px solid color-mix(in srgb, var(--brand) 22%, transparent); border-top-color: var(--brand); border-radius: 50%; animation: progress-spin .75s linear infinite; }@keyframes progress-spin { to { transform: rotate(360deg); } }
.route-search-progress progress { width: 100%; height: 8px; overflow: hidden; appearance: none; border: 0; border-radius: 999px; background: #e8ecf4; pointer-events: none; }.route-search-progress progress::-webkit-progress-bar { border-radius: 999px; background: #e8ecf4; }.route-search-progress progress::-webkit-progress-value { border-radius: 999px; background: linear-gradient(90deg, var(--brand), var(--accent)); }.route-search-progress progress::-moz-progress-bar { border-radius: 999px; background: linear-gradient(90deg, var(--brand), var(--accent)); }
.route-summary-heading { display: flex; justify-content: space-between; gap: 12px; }.route-summary-heading span { color: var(--muted); font-size: .86rem; }
.route-summary { display: grid; gap: 8px; margin: 0; padding: 0; list-style: none; }.route-summary li { display: grid; grid-template-columns: 30px minmax(0, 1fr) minmax(180px, auto); align-items: center; gap: 12px; padding: 12px; border: 1px solid var(--border); border-radius: 9px; background: var(--surface); }.leg-number { display: grid; place-items: center; width: 28px; height: 28px; border-radius: 50%; background: var(--brand-soft); color: var(--brand-strong); font-weight: 800; }.leg-route, .leg-outcome { display: grid; gap: 3px; }.leg-route span, .leg-outcome span { color: var(--muted); font-size: .8rem; }.leg-outcome { justify-items: end; text-align: right; }.leg-status--faresFound .leg-outcome strong { color: #177245; }.leg-status--noFares .leg-outcome strong, .leg-status--failed .leg-outcome strong, .leg-status--limited .leg-outcome strong { color: #b42318; }
.connection-note { margin: 0; padding: 11px 13px; border-radius: 8px; background: #fff8e8; color: #714500; }.connection-note--problem { background: #fff1f0; color: #8f1d18; }
.results-layout { display: grid; grid-template-columns: minmax(210px, 260px) minmax(0, 1fr); gap: 18px; margin-top: 22px; }.results-list { display: grid; gap: 14px; }
.pagination { display: flex; align-items: center; justify-content: center; gap: 10px; }.pagination button { padding: 8px 10px; border: 1px solid var(--border); border-radius: 7px; background: var(--surface); color: var(--ink-strong); }
@media (max-width: 680px) { .trip-options, .results-layout, .return-details { grid-template-columns: 1fr; }.return-details { padding-left: 0; }.progress-heading, .route-summary-heading { flex-direction: column; }.route-summary li { grid-template-columns: 30px minmax(0, 1fr); }.leg-outcome { grid-column: 2; justify-items: start; text-align: left; } }
</style>
