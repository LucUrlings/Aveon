<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AirportGroupPicker from '../components/flight-search/AirportGroupPicker.vue'
import { useAirportPicker } from '../features/flight-search/useAirportPicker'
import { getExploreRoutes } from '../features/explore/api'
import { localDateWithOffset, toLocalDateInputValue } from '../features/explore/localDate'
import RouteGlobe from '../features/explore/RouteGlobe.vue'
import type { ExploreAirport, ExploreRoutesResponse } from '../features/explore/types'
import { getItinerarySearchCapabilities } from '../features/itinerary-search/api'

const router = useRouter()
const route = useRoute()
const picker = useAirportPicker([])
const minimumLeaveDate = localDateWithOffset(0)
const maximumLeaveDate = localDateWithOffset(365)
const validDate = (value: unknown): value is string => {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const parsed = new Date(`${value}T12:00:00`)
  return !Number.isNaN(parsed.getTime())
    && toLocalDateInputValue(parsed) === value
    && value >= minimumLeaveDate
    && value <= maximumLeaveDate
}
const leaveDate = ref(validDate(route.query.date) ? route.query.date : localDateWithOffset(1))
const routes = ref<ExploreRoutesResponse | null>(null)
const loading = ref(false)
const error = ref('')
const destinationQuery = ref('')
const committedPath = ref<ExploreAirport[]>([])
const selectedDestination = ref<ExploreAirport | null>(null)
const hoveredDestination = ref<ExploreAirport | null>(null)
const globe = ref<InstanceType<typeof RouteGlobe> | null>(null)
const maxOrderedLegs = ref(8)
let suppressedPickerCode = ''
let controller: AbortController | null = null
let requestedPath = ''
let loadedPath = ''

const pathCodes = computed(() => committedPath.value.map(airport => airport.code))
const previewPath = computed(() => selectedDestination.value ? [...committedPath.value, selectedDestination.value] : committedPath.value)
const builderPath = computed(() => previewPath.value.length - 1 <= maxOrderedLegs.value ? previewPath.value : committedPath.value)
const routeLabel = computed(() => previewPath.value.map(airport => airport.code).join(' → '))
const isOneLegSelection = computed(() => committedPath.value.length === 1 && Boolean(selectedDestination.value))
const canCommitSelection = computed(() => Boolean(selectedDestination.value) && committedPath.value.length <= maxOrderedLegs.value)
const canContinueInBuilder = computed(() => Boolean(selectedDestination.value) && committedPath.value.length >= 2 && builderPath.value.length >= 2)
const airportOption = (airport: ExploreAirport) => ({ code: airport.code, name: airport.name, displayLabel: `${airport.name} (${airport.code})` })
const queryPath = () => typeof route.query.path === 'string' ? route.query.path.split(',').map(code => code.trim().toUpperCase()).filter(Boolean) : []
const validPath = (codes: string[]) => codes.length > 0 && codes.length <= maxOrderedLegs.value + 1 && codes.every(code => /^[A-Z]{3}$/.test(code)) && new Set(codes).size === codes.length
const retryLoad = () => {
  const urlPath = queryPath()
  const codes = urlPath.length ? urlPath : pathCodes.value.length ? pathCodes.value : picker.airports.value.map(airport => airport.code)
  void loadPath(codes)
}

const filteredDestinations = computed(() => {
  const query = destinationQuery.value.trim().toLowerCase()
  if (!routes.value || !query) return routes.value?.destinations ?? []
  return routes.value.destinations.filter(destination =>
    destination.code.toLowerCase().includes(query)
    || destination.city.toLowerCase().includes(query)
    || destination.country.toLowerCase().includes(query)
    || destination.name.toLowerCase().includes(query))
})

const loadPath = async (codes: string[]) => {
  if (!validPath(codes)) { error.value = 'This Explore path is invalid or exceeds the route limit.'; return }
  if (!validDate(leaveDate.value)) { error.value = 'Choose a valid leave date.'; return }
  controller?.abort()
  const requestController = new AbortController()
  requestedPath = `${codes.join(',')}|${leaveDate.value}`
  controller = requestController
  selectedDestination.value = null
  hoveredDestination.value = null
  destinationQuery.value = ''
  loading.value = true
  error.value = ''
  try {
    const responses: ExploreRoutesResponse[] = [await getExploreRoutes(codes[0], leaveDate.value, requestController.signal)]
    for (const code of codes.slice(1)) responses.push(await getExploreRoutes(code, undefined, requestController.signal))
    responses.forEach((response, index) => {
      if (response.origin.code !== codes[index]) throw new Error(`The schedule response for ${codes[index]} returned a different airport.`)
    })
    const airports: ExploreAirport[] = [responses[0].origin]
    for (let index = 1; index < responses.length; index += 1) {
      const destination = responses[index - 1].destinations.find(airport => airport.code === codes[index])
      if (!destination) throw new Error(`No current direct schedule was found from ${codes[index - 1]} to ${codes[index]}. Remove that stop and choose a listed destination.`)
      airports.push(destination)
    }
    committedPath.value = airports
    routes.value = responses.at(-1) ?? null
    loadedPath = `${codes.join(',')}|${leaveDate.value}`
    if (airports[0] && picker.airports.value[0]?.code !== airports[0].code) {
      suppressedPickerCode = airports[0].code
      picker.airports.value = [airportOption(airports[0])]
    }
  } catch (reason) {
    if (!(reason instanceof Error && reason.name === 'AbortError')) {
      routes.value = null
      error.value = reason instanceof Error ? reason.message : 'Could not load this route network.'
    }
  } finally {
    if (controller === requestController) { loading.value = false; requestedPath = '' }
  }
}

watch(() => picker.airports.value[0]?.code, code => {
  if (code && code === suppressedPickerCode) { suppressedPickerCode = ''; return }
  if (code && code === committedPath.value[0]?.code && routes.value) return
  if (code) { void loadPath([code]); void router.replace({ path: '/explore', query: { path: code, date: leaveDate.value } }) }
  else { controller?.abort(); routes.value = null; committedPath.value = []; selectedDestination.value = null; error.value = ''; void router.replace({ path: '/explore' }) }
})

watch(() => [route.query.path, route.query.date], () => {
  if (validDate(route.query.date)) leaveDate.value = route.query.date
  const codes = queryPath()
  const requestKey = `${codes.join(',')}|${leaveDate.value}`
  if (codes.length && requestKey !== loadedPath && requestKey !== requestedPath) void loadPath(codes)
}, { immediate: true })

const changeLeaveDate = () => {
  const codes = queryPath().length ? queryPath() : pathCodes.value.length ? pathCodes.value : picker.airports.value.map(airport => airport.code)
  if (!codes.length || !validDate(leaveDate.value)) return
  void router.replace({ path: '/explore', query: { path: codes.join(','), date: leaveDate.value } })
  void loadPath(codes)
}

const selectDestination = (destination: ExploreAirport) => {
  if (pathCodes.value.includes(destination.code)) return
  selectedDestination.value = destination
  globe.value?.focusDestination(destination)
}

const randomize = () => {
  if (!routes.value?.destinations.length) return
  const choices = routes.value.destinations.filter(destination => !pathCodes.value.includes(destination.code) && destination.code !== selectedDestination.value?.code)
  const pool = choices.length ? choices : routes.value.destinations.filter(destination => !pathCodes.value.includes(destination.code))
  if (pool.length) selectDestination(pool[Math.floor(Math.random() * pool.length)])
}

const searchFares = () => {
  if (!selectedDestination.value || committedPath.value.length !== 1) return
  void router.push({ path: '/search', query: { origins: committedPath.value[0].code, destinations: selectedDestination.value.code, dates: leaveDate.value, prefill: 'true' } })
}
const commitSelection = () => {
  if (!selectedDestination.value || !canCommitSelection.value) return
  const nextPath = [...committedPath.value, selectedDestination.value]
  const codes = nextPath.map(airport => airport.code)
  committedPath.value = nextPath
  selectedDestination.value = null
  hoveredDestination.value = null
  void router.push({ path: '/explore', query: { path: codes.join(','), date: leaveDate.value } })
  void loadPath(codes)
}
const truncatePath = (index: number) => void router.push({ path: '/explore', query: { path: pathCodes.value.slice(0, index + 1).join(','), date: leaveDate.value } })
const continueInBuilder = () => {
  if (!canContinueInBuilder.value) return
  void router.push({ path: '/multi-destination', query: { mode: 'ordered', route: builderPath.value.map(airport => airport.code).join(','), departureDate: leaveDate.value, source: 'explore', prefill: 'true' } })
}
const clearSelection = () => { selectedDestination.value = null; hoveredDestination.value = null }

onBeforeUnmount(() => controller?.abort())
onMounted(async () => {
  try {
    const capabilities = await getItinerarySearchCapabilities()
    if (capabilities.maxOrderedLegs > 0) {
      maxOrderedLegs.value = capabilities.maxOrderedLegs
      if (committedPath.value.length - 1 > maxOrderedLegs.value) truncatePath(maxOrderedLegs.value)
    }
  } catch { /* The route builder remains authoritative if configuration is unavailable. */ }
})
</script>

<template>
  <main id="main-content" class="explore-page" tabindex="-1">
    <header class="explore-heading">
      <p class="eyebrow">Explore direct routes</p>
      <h1>Start somewhere. See where it leads.</h1>
      <p>Choose an airport and leave date to see where direct flights are scheduled that day. Explore does not check fares; availability is confirmed only when you continue to search.</p>
    </header>

    <section class="origin-panel" aria-labelledby="origin-heading">
      <div><p class="eyebrow">Starting point</p><h2 id="origin-heading">Where are you flying from?</h2></div>
      <AirportGroupPicker
        v-model:input="picker.input.value"
        v-model:airports="picker.airports.value"
        label="Starting airport"
        input-aria-label="Choose an airport to explore from"
        suggestions-aria-label="Explore origin suggestions"
        suggestion-id-prefix="explore-origin"
        :suggestions="picker.suggestions.value"
        :suggestions-loading="picker.suggestionsLoading.value"
        :suggestions-error="picker.suggestionsError.value"
        :has-searched-suggestions="picker.hasSearchedSuggestions.value"
        :max-airports="1"
        @add-airport="picker.addAirport"
        @remove-airport="picker.removeAirport"
        @confirm-input="picker.confirmInput"
      />
      <label class="leave-date">Leave date<input v-model="leaveDate" type="date" :min="minimumLeaveDate" :max="maximumLeaveDate" required @change="changeLeaveDate" /></label>
    </section>

    <Transition name="route-tray">
      <nav v-if="committedPath.length" class="route-tray" aria-label="Explored route">
        <span>Your route</span>
        <ol><li v-for="(airport, index) in committedPath" :key="airport.code"><button type="button" :aria-label="`Return to ${airport.city}`" @click="truncatePath(index)">{{ airport.code }}</button><b v-if="index < committedPath.length - 1" aria-hidden="true">→</b></li></ol>
        <button v-if="committedPath.length > 1" type="button" class="tray-recovery" @click="truncatePath(committedPath.length - 2)">Remove last stop</button>
      </nav>
    </Transition>

    <Transition name="explore-feedback" mode="out-in">
      <section v-if="loading" key="loading" class="explore-status" role="status"><span class="loading-spinner" aria-hidden="true" /><strong>{{ routes ? 'Updating direct destinations…' : 'Mapping direct destinations…' }}</strong></section>
      <section v-else-if="error" key="error" class="explore-status explore-status--error" role="alert"><strong>Could not map this airport</strong><p>{{ error }}</p><button type="button" @click="retryLoad">Try again</button></section>
    </Transition>
    <Transition name="explore-results" mode="out-in">
    <section v-if="routes" :key="routes.origin.code" class="explore-results" :class="{ 'explore-results--updating': loading }" :aria-busy="loading" aria-labelledby="routes-heading">
      <div class="network-heading">
        <div><p class="eyebrow">{{ committedPath.length === 1 ? `Scheduled ${leaveDate}` : 'Onward route suggestions' }}</p><h2 id="routes-heading">{{ routes.origin.city }} connects directly to {{ routes.destinations.length }} destination{{ routes.destinations.length === 1 ? '' : 's' }}</h2></div>
        <button type="button" class="randomize-button" :disabled="routes.destinations.length === 0" @click="randomize">Surprise me</button>
      </div>
      <p v-if="routes.isStale" class="network-note network-note--warning">FlightAPI is temporarily unavailable, so this map uses the latest cached schedule.</p>
      <p v-if="!routes.isComplete" class="network-note network-note--warning">Some scheduled destinations could not be included, so this map is partial.</p>
      <p v-if="committedPath.length === 1" class="network-note">Direct departures scheduled for {{ leaveDate }}. Routes may include codeshares, but each destination appears once.</p>
      <p v-else class="network-note network-note--warning">Onward destinations are route-network suggestions and are not checked against later travel dates. Choose and validate every remaining date in Build my route.</p>

      <div v-if="routes.destinations.length" class="explore-grid">
        <div class="globe-column">
          <Transition name="route-selection">
          <article v-if="selectedDestination" class="route-selection" aria-live="polite">
            <div><span>{{ isOneLegSelection ? 'Selected direct route' : 'Route preview' }}</span><strong>{{ routeLabel }}</strong><small>{{ selectedDestination.city }}, {{ selectedDestination.country }}</small></div>
            <p>These scheduled edges were observed independently. Fares, matching dates, protected connections, visa eligibility, and a through-ticket are not guaranteed.</p>
            <div class="selection-actions">
              <button v-if="isOneLegSelection" type="button" class="primary-selection" @click="searchFares">Search fares</button>
              <button v-if="canContinueInBuilder" type="button" class="primary-selection" @click="continueInBuilder">Continue in Build my route</button>
              <button type="button" :disabled="!canCommitSelection" @click="commitSelection">{{ committedPath.length === 1 ? `Explore onward from ${selectedDestination.code}` : `Keep exploring from ${selectedDestination.code}` }}</button>
              <button type="button" @click="clearSelection">Clear selection</button>
            </div>
            <small v-if="!canCommitSelection" class="limit-note">You have reached Build my route's maximum number of flight legs. Continue opens the committed route without this extra candidate.</small>
          </article>
          </Transition>
          <RouteGlobe ref="globe" :routes="routes" :committed-path="committedPath" :selected-destination="selectedDestination" :hovered-destination="hoveredDestination" @select="selectDestination" @hover="hoveredDestination = $event" />
        </div>
        <aside class="destination-browser" aria-labelledby="destination-list-heading">
          <label>Find a destination<input v-model="destinationQuery" type="search" placeholder="City, country, or airport code" /></label>
          <div class="destination-list-heading"><strong id="destination-list-heading">Direct destinations</strong><span>{{ filteredDestinations.length }}</span></div>
          <ul>
            <TransitionGroup name="destination">
              <li v-for="destination in filteredDestinations" :key="destination.code"><button type="button" :disabled="pathCodes.includes(destination.code)" :aria-pressed="selectedDestination?.code === destination.code" @click="selectDestination(destination)" @focus="hoveredDestination = destination" @blur="hoveredDestination = null" @mouseenter="hoveredDestination = destination" @mouseleave="hoveredDestination = null" @keydown.enter.prevent="selectDestination(destination)" @keydown.space.prevent="selectDestination(destination)"><span><strong>{{ destination.city }}</strong><small>{{ destination.name }}</small></span><b>{{ destination.code }}</b></button></li>
            </TransitionGroup>
          </ul>
          <p v-if="filteredDestinations.length === 0">No destinations match that filter.</p>
        </aside>
      </div>
      <div v-else class="empty-network"><strong>No direct destinations were found</strong><p>Try another airport or retry when a newer schedule is available.</p></div>
    </section>
    </Transition>
  </main>
</template>

<style scoped>
.explore-page { width: min(1240px, calc(100% - 48px)); margin: 0 auto; padding: 58px 0 80px; }.explore-heading { max-width: 790px; }.eyebrow { margin: 0 0 8px; color: var(--brand); font-size: .72rem; font-weight: 850; letter-spacing: .14em; text-transform: uppercase; }.explore-heading h1 { max-width: 760px; margin: 0; color: var(--ink-strong); font-size: clamp(2.6rem, 6vw, 5.2rem); letter-spacing: -.055em; line-height: .98; }.explore-heading > p:last-child { color: var(--muted); line-height: 1.7; }.origin-panel { display: grid; grid-template-columns: minmax(220px, .65fr) minmax(300px, 1fr) minmax(150px, .45fr); gap: 22px; align-items: end; margin-top: 38px; padding: 20px; border: 1px solid var(--border); border-radius: 18px; background: var(--surface-raised); box-shadow: var(--shadow-sm); }.origin-panel h2 { margin: 0; color: var(--ink-strong); }.leave-date { display: grid; gap: 6px; color: var(--muted); font-size: .84rem; }.leave-date input { min-height: 44px; padding: 10px; border: 1px solid var(--border); border-radius: 9px; background: var(--surface); color: var(--ink-strong); }.explore-status { display: flex; align-items: center; gap: 12px; margin-top: 24px; padding: 24px; border: 1px solid var(--border); border-radius: 16px; background: var(--surface-raised); }.explore-status--error { display: grid; }.explore-status button, .randomize-button, .route-selection button { justify-self: start; border: 0; border-radius: 10px; padding: 10px 14px; background: var(--brand); color: white; font-weight: 750; cursor: pointer; }.route-selection button:disabled { cursor: not-allowed; opacity: .45; }.loading-spinner { width: 18px; height: 18px; border: 2px solid color-mix(in srgb, var(--brand) 22%, transparent); border-top-color: var(--brand); border-radius: 50%; animation: spin .75s linear infinite; }@keyframes spin { to { transform: rotate(360deg); } }.explore-results { margin-top: 36px; }.network-heading { display: flex; align-items: end; justify-content: space-between; gap: 20px; }.network-heading h2 { max-width: 780px; margin: 0; color: var(--ink-strong); font-size: clamp(1.7rem, 3vw, 2.65rem); letter-spacing: -.035em; }.randomize-button { flex: 0 0 auto; background: var(--ink-strong); }.randomize-button:disabled { opacity: .45; }.network-note { margin: 12px 0 0; color: var(--muted); font-size: .85rem; }.network-note--warning { padding: 10px 12px; border-left: 3px solid #d98b00; background: #fff8e8; color: #714500; }.route-tray { display: flex; align-items: center; gap: 12px; margin-top: 18px; padding: 12px 14px; border: 1px solid var(--border); border-radius: 14px; background: var(--surface-raised); }.route-tray > span { color: var(--muted); font-size: .8rem; font-weight: 700; text-transform: uppercase; }.route-tray ol { display: flex; align-items: center; gap: 7px; margin: 0; padding: 0; list-style: none; }.route-tray li { display: flex; align-items: center; gap: 7px; }.route-tray li button, .tray-recovery { border: 0; background: transparent; color: var(--brand-strong); font-weight: 800; cursor: pointer; }.tray-recovery { margin-left: auto; color: var(--muted); }.explore-grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(280px, 340px); align-items: start; gap: 18px; margin-top: 18px; }.globe-column { min-width: 0; }.globe-column :deep(.globe-shell) { height: 600px; min-height: 600px; border: 1px solid rgba(99, 102, 241, .18); background: radial-gradient(circle at 50% 45%, rgba(79, 70, 229, .21), transparent 58%), #f7f8ff; }.route-selection { display: grid; gap: 10px; margin: 0 0 12px; padding: 16px; border: 1px solid var(--brand); border-radius: 14px; background: var(--surface-raised); box-shadow: var(--shadow-sm); }.route-selection > div:first-child { display: grid; gap: 2px; }.route-selection span, .route-selection small, .route-selection p { color: var(--muted); }.route-selection p { margin: 0; font-size: .84rem; line-height: 1.5; }.selection-actions { display: flex; flex-wrap: wrap; gap: 8px; }.selection-actions button:not(.primary-selection) { border: 1px solid var(--border); background: var(--surface); color: var(--ink-strong); }.limit-note { color: #9a5b00 !important; }.destination-browser { display: flex; max-height: 600px; flex-direction: column; padding: 16px; border: 1px solid var(--border); border-radius: 18px; background: var(--surface-raised); }.destination-browser label { display: grid; gap: 6px; color: var(--muted); font-size: .84rem; }.destination-browser input { padding: 10px; border: 1px solid var(--border); border-radius: 9px; background: var(--surface); }.destination-list-heading { display: flex; justify-content: space-between; margin: 18px 0 8px; }.destination-list-heading span { color: var(--muted); }.destination-browser ul { display: grid; gap: 6px; overflow: auto; margin: 0; padding: 0; list-style: none; }.destination-browser li button { display: flex; width: 100%; align-items: center; justify-content: space-between; gap: 10px; padding: 10px; border: 1px solid transparent; border-radius: 9px; background: transparent; color: var(--ink-strong); text-align: left; cursor: pointer; }.destination-browser li button:hover, .destination-browser li button:focus-visible, .destination-browser li button[aria-pressed='true'] { border-color: var(--brand); background: var(--brand-soft); }.destination-browser li button:disabled { cursor: not-allowed; opacity: .45; }.destination-browser li span { display: grid; min-width: 0; gap: 2px; }.destination-browser small { overflow: hidden; color: var(--muted); text-overflow: ellipsis; white-space: nowrap; }.destination-browser b { color: var(--brand-strong); }.empty-network { margin-top: 18px; padding: 44px 20px; border: 1px dashed var(--border); border-radius: 18px; text-align: center; }.empty-network p { color: var(--muted); }
.explore-results { transition: opacity .28s ease, transform .28s ease, filter .28s ease; }.explore-results--updating { pointer-events: none; opacity: .58; filter: saturate(.72); transform: scale(.995); }.explore-feedback-enter-active, .explore-feedback-leave-active, .route-tray-enter-active, .route-tray-leave-active { overflow: hidden; transition: opacity .22s ease, transform .22s ease, max-height .28s ease, margin .28s ease, padding .28s ease; }.explore-feedback-enter-from, .explore-feedback-leave-to, .route-tray-enter-from, .route-tray-leave-to { max-height: 0; margin-top: 0; padding-top: 0; padding-bottom: 0; opacity: 0; transform: translateY(-8px); }.explore-feedback-enter-to, .explore-feedback-leave-from { max-height: 220px; }.route-tray-enter-to, .route-tray-leave-from { max-height: 90px; }.explore-results-enter-active, .explore-results-leave-active { transition: opacity .3s ease, transform .3s ease; }.explore-results-enter-from, .explore-results-leave-to { opacity: 0; transform: translateY(12px); }.route-selection-enter-active, .route-selection-leave-active { overflow: hidden; transition: opacity .22s ease, transform .28s ease, max-height .32s ease, margin .32s ease, padding .32s ease; }.route-selection-enter-from, .route-selection-leave-to { max-height: 0; margin-bottom: 0; padding-top: 0; padding-bottom: 0; opacity: 0; transform: translateY(-10px); }.route-selection-enter-to, .route-selection-leave-from { max-height: 420px; }.destination-move, .destination-enter-active, .destination-leave-active { transition: opacity .2s ease, transform .24s ease; }.destination-enter-from, .destination-leave-to { opacity: 0; transform: translateX(10px); }.destination-leave-active { position: absolute; width: calc(100% - 32px); }
.destination-browser ul { position: relative; }
@media (prefers-reduced-motion: reduce) { .loading-spinner { animation: none; }.explore-results, .explore-feedback-enter-active, .explore-feedback-leave-active, .route-tray-enter-active, .route-tray-leave-active, .explore-results-enter-active, .explore-results-leave-active, .route-selection-enter-active, .route-selection-leave-active, .destination-move, .destination-enter-active, .destination-leave-active { transition: none; transform: none; } }
@media (max-width: 820px) { .origin-panel, .explore-grid { grid-template-columns: 1fr; }.destination-browser { max-height: 480px; }.network-heading { align-items: flex-start; flex-direction: column; }.globe-column :deep(.globe-shell) { height: 480px; min-height: 480px; } }
@media (max-width: 600px) { .explore-page { width: min(100% - 28px, 1240px); padding-top: 40px; }.origin-panel { padding: 15px; }.route-tray { align-items: flex-start; flex-wrap: wrap; }.tray-recovery { margin-left: 0; }.selection-actions { flex-direction: column; }.selection-actions button { width: 100%; }.globe-column :deep(.globe-shell) { height: 360px; min-height: 360px; } }
</style>
