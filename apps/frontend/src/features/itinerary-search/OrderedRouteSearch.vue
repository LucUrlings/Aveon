<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { getItinerarySearch, startItinerarySearch } from './api'
import ItineraryFiltersPanel from './ItineraryFiltersPanel.vue'
import ItineraryResultCard from './ItineraryResultCard.vue'
import OrderedLegEditor, { type OrderedLegModel } from './OrderedLegEditor.vue'
import type { ItineraryResultsQuery, ItinerarySearchSession, OrderedTripRequest, Ranking } from './types'

let sequence = 1
const nextDate = () => {
  const date = new Date(); date.setDate(date.getDate() + sequence)
  return date.toISOString().slice(0, 10)
}
const createLeg = (): OrderedLegModel => ({ id: `ordered-leg-${sequence++}`, from: [], to: [], departureDate: nextDate(), continuity: 'sameAirport' })
const legs = ref<OrderedLegModel[]>([createLeg()])
const adults = ref(1)
const cabinClass = ref('economy')
const ranking = ref<Ranking>('recommended')
const session = ref<ItinerarySearchSession | null>(null)
const query = ref<ItineraryResultsQuery>({ ranking: 'recommended', pageSize: 25, allowAirportSwitches: true })
const error = ref('')
const submitting = ref(false)
let timer: number | undefined
let controller: AbortController | null = null
const isRunning = computed(() => session.value?.status === 'running')

const updateLeg = (index: number, leg: OrderedLegModel) => { legs.value[index] = leg }
const addLeg = () => { legs.value = [...legs.value, createLeg()] }
const removeLeg = (index: number) => { legs.value = legs.value.filter((_, position) => position !== index) }
const group = (id: string, airports: OrderedLegModel['from']) => ({ id, label: airports.map(airport => airport.displayLabel).join(', '), airportCodes: airports.map(airport => airport.code) })

const refresh = async (poll = false) => {
  if (!session.value?.searchId) return
  controller?.abort(); controller = new AbortController()
  try {
    session.value = await getItinerarySearch(session.value.searchId, query.value, controller.signal)
    if (poll && session.value.status === 'running') timer = window.setTimeout(() => refresh(true), 700)
  } catch (reason) {
    if (!(reason instanceof Error && reason.name === 'AbortError')) error.value = reason instanceof Error ? reason.message : 'Could not load results.'
  }
}

const submit = async () => {
  error.value = ''
  if (legs.value.some(leg => !leg.departureDate || leg.from.length === 0 || leg.to.length === 0)) { error.value = 'Add a date and at least one airport to both ends of every flight.'; return }
  submitting.value = true
  controller?.abort(); controller = new AbortController()
  const request: OrderedTripRequest = {
    mode: 'ordered', adults: adults.value, cabinClass: cabinClass.value, ranking: ranking.value,
    legs: legs.value.map((leg, index) => ({ id: leg.id, from: group(`${leg.id}-from`, leg.from), to: group(`${leg.id}-to`, leg.to), departureDate: leg.departureDate, airportContinuityWithPrevious: index === 0 ? 'sameAirport' : leg.continuity })),
  }
  try {
    session.value = await startItinerarySearch(request, controller.signal)
    query.value = { ...query.value, ranking: ranking.value }
    timer = window.setTimeout(() => refresh(true), 100)
  } catch (reason) { error.value = reason instanceof Error ? reason.message : 'Could not start the search.' }
  finally { submitting.value = false }
}

watch(query, () => { if (session.value?.searchId) refresh(false) }, { deep: true })
onBeforeUnmount(() => { if (timer) window.clearTimeout(timer); controller?.abort() })
</script>

<template>
  <section aria-label="Build my route form">
    <form class="ordered-form" @submit.prevent="submit">
      <OrderedLegEditor v-for="(leg, index) in legs" :key="leg.id" :model-value="leg" :index="index" :removable="legs.length > 1" @update:model-value="updateLeg(index, $event)" @remove="removeLeg(index)" />
      <button type="button" class="secondary-action" @click="addLeg">Add another flight</button>
      <div class="trip-options">
        <label>Travellers<input v-model.number="adults" type="number" min="1" max="9" /></label>
        <label>Cabin<select v-model="cabinClass"><option value="economy">Economy</option><option value="premium_economy">Premium economy</option><option value="business">Business</option><option value="first">First</option></select></label>
        <label>Ranking<select v-model="ranking"><option value="recommended">Recommended</option><option value="cheapest">Cheapest</option><option value="fastest">Fastest</option></select></label>
      </div>
      <p v-if="error" role="alert" class="form-error">{{ error }}</p>
      <button class="primary-action" type="submit" :disabled="submitting">{{ submitting ? 'Starting…' : 'Search complete route' }}</button>
    </form>

    <section v-if="session" class="search-status" aria-live="polite">
      <p v-if="isRunning">Searching airport pairs… {{ session.progress }}%</p>
      <p v-else-if="session.status === 'failed'" role="alert">{{ session.errorMessage ?? 'Search failed.' }}</p>
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
.secondary-action, .primary-action { justify-self: start; padding: 10px 14px; border-radius: 8px; cursor: pointer; }
.secondary-action { border: 1px solid var(--border); background: var(--surface); color: var(--ink-strong); }
.primary-action { border: 0; background: var(--brand); color: white; font-weight: 700; }
.primary-action:disabled { opacity: .6; cursor: wait; }
.trip-options { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.trip-options label { display: grid; gap: 5px; color: var(--muted); }
.trip-options input, .trip-options select { padding: 9px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface); color: var(--ink-strong); }
.form-error { color: #b42318; }.form-warning { color: #9b5c00; }.search-status { margin-top: 18px; }
.results-layout { display: grid; grid-template-columns: minmax(210px, 260px) minmax(0, 1fr); gap: 18px; margin-top: 22px; }.results-list { display: grid; gap: 14px; }
.pagination { display: flex; align-items: center; justify-content: center; gap: 10px; }.pagination button { padding: 8px 10px; border: 1px solid var(--border); border-radius: 7px; background: var(--surface); color: var(--ink-strong); }
@media (max-width: 680px) { .trip-options, .results-layout { grid-template-columns: 1fr; } }
</style>
