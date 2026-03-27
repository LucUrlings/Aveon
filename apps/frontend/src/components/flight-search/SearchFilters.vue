<script setup lang="ts">
import { ref } from 'vue'

defineProps<{
  providerFilters: string[]
  airlineFilters: string[]
  departureAirportFilters: string[]
  arrivalAirportFilters: string[]
  availablePriceRange: [number, number]
  availableMaxDurationMinutes: number
}>()

const priceRange = defineModel<[number, number]>('priceRange', { required: true })
const maxDurationMinutes = defineModel<number>('maxDurationMinutes', { required: true })
const includeDirectFlights = defineModel<boolean>('includeDirectFlights', { required: true })
const includeOneStopFlights = defineModel<boolean>('includeOneStopFlights', { required: true })
const includeTwoPlusStopFlights = defineModel<boolean>('includeTwoPlusStopFlights', { required: true })
const selectedProviders = defineModel<string[]>('selectedProviders', { required: true })
const selectedAirlines = defineModel<string[]>('selectedAirlines', { required: true })
const selectedDepartureAirports = defineModel<string[]>('selectedDepartureAirports', { required: true })
const selectedArrivalAirports = defineModel<string[]>('selectedArrivalAirports', { required: true })
const departureTimeRange = defineModel<[number, number]>('departureTimeRange', { required: true })
const arrivalTimeRange = defineModel<[number, number]>('arrivalTimeRange', { required: true })

const expandedSections = ref({
  price: true,
  duration: true,
  stops: true,
  departure: true,
  arrival: true,
  departureAirports: false,
  arrivalAirports: false,
  sources: false,
  airlines: false,
})

const formatMinutes = (minutes: number) => {
  if (minutes >= 1440) {
    return '23:59'
  }

  const hours = String(Math.floor(minutes / 60)).padStart(2, '0')
  const mins = String(minutes % 60).padStart(2, '0')
  return `${hours}:${mins}`
}

const formatDuration = (minutes: number) => {
  const safeMinutes = Math.max(minutes, 0)
  const hours = Math.floor(safeMinutes / 60)
  const mins = safeMinutes % 60

  if (hours === 0) {
    return `${mins}m`
  }

  if (mins === 0) {
    return `${hours}h`
  }

  return `${hours}h ${mins}m`
}

const ensureOrderedRange = (
  range: [number, number],
  changedIndex: 0 | 1,
  rawValue: number,
  minValue: number,
  maxValue: number,
) => {
  const nextValue = Math.min(maxValue, Math.max(minValue, rawValue))

  if (changedIndex === 0) {
    range[0] = Math.min(nextValue, range[1])
    return
  }

  range[1] = Math.max(nextValue, range[0])
}

const getRangeStyle = (range: [number, number]) => {
  const start = (range[0] / 1440) * 100
  const end = (range[1] / 1440) * 100

  return {
    left: `${start}%`,
    width: `${Math.max(end - start, 0)}%`,
  }
}

const getPriceRangeStyle = (range: [number, number], availableRange: [number, number]) => {
  const [minPrice, maxPrice] = availableRange
  const span = Math.max(maxPrice - minPrice, 1)
  const start = ((range[0] - minPrice) / span) * 100
  const end = ((range[1] - minPrice) / span) * 100

  return {
    left: `${Math.max(start, 0)}%`,
    width: `${Math.max(end - start, 0)}%`,
  }
}

const toggleSection = (section: keyof typeof expandedSections.value) => {
  expandedSections.value[section] = !expandedSections.value[section]
}
</script>

<template>
  <aside class="filters-panel">
    <div class="filters-card">
      <p class="eyebrow">Filters</p>
      <h3>Refine results</h3>

      <section class="filter-section" :class="{ open: expandedSections.price }">
        <button type="button" class="filter-section-summary" @click="toggleSection('price')">Price range</button>
        <div class="filter-section-body" :class="{ open: expandedSections.price }">
          <div class="filter-section-inner time-filter-group">
            <div class="time-filter-header">
              <span class="filter-label">Price range</span>
              <strong>EUR {{ priceRange[0] }} - EUR {{ priceRange[1] }}</strong>
            </div>
            <div class="range-slider">
              <div class="range-slider-track" />
              <div class="range-slider-selected" :style="getPriceRangeStyle(priceRange, availablePriceRange)" />
              <input
                :key="`price-min-${availablePriceRange[0]}-${availablePriceRange[1]}`"
                :value="priceRange[0]"
                type="range"
                :min="availablePriceRange[0]"
                :max="availablePriceRange[1]"
                step="1"
                @input="ensureOrderedRange(priceRange, 0, Number(($event.target as HTMLInputElement).value), availablePriceRange[0], availablePriceRange[1])"
              />
              <input
                :key="`price-max-${availablePriceRange[0]}-${availablePriceRange[1]}`"
                :value="priceRange[1]"
                type="range"
                :min="availablePriceRange[0]"
                :max="availablePriceRange[1]"
                step="1"
                @input="ensureOrderedRange(priceRange, 1, Number(($event.target as HTMLInputElement).value), availablePriceRange[0], availablePriceRange[1])"
              />
            </div>
          </div>
        </div>
      </section>

      <section class="filter-section" :class="{ open: expandedSections.stops }">
        <button type="button" class="filter-section-summary" @click="toggleSection('stops')">Stops</button>
        <div class="filter-section-body" :class="{ open: expandedSections.stops }">
          <div class="filter-section-inner stop-filter-group">
            <label class="filter-toggle">
              <input v-model="includeDirectFlights" type="checkbox" />
              <span>Direct flights</span>
            </label>
            <label class="filter-toggle">
              <input v-model="includeOneStopFlights" type="checkbox" />
              <span>Include 1 stop</span>
            </label>
            <label class="filter-toggle">
              <input v-model="includeTwoPlusStopFlights" type="checkbox" />
              <span>Include 2+ stops</span>
            </label>
          </div>
        </div>
      </section>

      <section class="filter-section" :class="{ open: expandedSections.duration }">
        <button type="button" class="filter-section-summary" @click="toggleSection('duration')">Max duration</button>
        <div class="filter-section-body" :class="{ open: expandedSections.duration }">
          <div class="filter-section-inner time-filter-group">
            <div class="time-filter-header">
              <span class="filter-label">Max duration</span>
              <strong>{{ formatDuration(maxDurationMinutes) }}</strong>
            </div>
            <div class="single-range-slider">
              <div class="range-slider-track" />
              <div
                class="range-slider-selected"
                :style="{ left: '0%', width: `${availableMaxDurationMinutes > 0 ? (maxDurationMinutes / availableMaxDurationMinutes) * 100 : 0}%` }"
              />
              <input
                :value="maxDurationMinutes"
                type="range"
                min="0"
                :max="availableMaxDurationMinutes"
                step="15"
                @input="maxDurationMinutes = Number(($event.target as HTMLInputElement).value)"
              />
            </div>
          </div>
        </div>
      </section>

      <section class="filter-section" :class="{ open: expandedSections.departure }">
        <button type="button" class="filter-section-summary" @click="toggleSection('departure')">Departure time</button>
        <div class="filter-section-body" :class="{ open: expandedSections.departure }">
          <div class="filter-section-inner time-filter-group">
            <div class="time-filter-header">
              <span class="filter-label">Departure time</span>
              <strong>{{ formatMinutes(departureTimeRange[0]) }} - {{ formatMinutes(departureTimeRange[1]) }}</strong>
            </div>
            <div class="range-slider">
              <div class="range-slider-track" />
              <div class="range-slider-selected" :style="getRangeStyle(departureTimeRange)" />
              <input
                :value="departureTimeRange[0]"
                type="range"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(departureTimeRange, 0, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
              <input
                :value="departureTimeRange[1]"
                type="range"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(departureTimeRange, 1, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
            </div>
          </div>
        </div>
      </section>

      <section class="filter-section" :class="{ open: expandedSections.arrival }">
        <button type="button" class="filter-section-summary" @click="toggleSection('arrival')">Arrival time</button>
        <div class="filter-section-body" :class="{ open: expandedSections.arrival }">
          <div class="filter-section-inner time-filter-group">
            <div class="time-filter-header">
              <span class="filter-label">Arrival time</span>
              <strong>{{ formatMinutes(arrivalTimeRange[0]) }} - {{ formatMinutes(arrivalTimeRange[1]) }}</strong>
            </div>
            <div class="range-slider">
              <div class="range-slider-track" />
              <div class="range-slider-selected" :style="getRangeStyle(arrivalTimeRange)" />
              <input
                :value="arrivalTimeRange[0]"
                type="range"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(arrivalTimeRange, 0, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
              <input
                :value="arrivalTimeRange[1]"
                type="range"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(arrivalTimeRange, 1, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
            </div>
          </div>
        </div>
      </section>

      <section class="filter-section" :class="{ open: expandedSections.departureAirports }">
        <button type="button" class="filter-section-summary" @click="toggleSection('departureAirports')">Departure airport</button>
        <div class="filter-section-body" :class="{ open: expandedSections.departureAirports }">
          <div class="filter-section-inner provider-filter-group">
            <template v-if="departureAirportFilters.length">
              <label
                v-for="airport in departureAirportFilters"
                :key="airport"
                class="filter-toggle"
              >
                <input v-model="selectedDepartureAirports" :value="airport" type="checkbox" />
                <span>{{ airport }}</span>
              </label>
            </template>
            <p v-else class="filter-placeholder">Available after results load</p>
          </div>
        </div>
      </section>

      <section class="filter-section" :class="{ open: expandedSections.arrivalAirports }">
        <button type="button" class="filter-section-summary" @click="toggleSection('arrivalAirports')">Arrival airport</button>
        <div class="filter-section-body" :class="{ open: expandedSections.arrivalAirports }">
          <div class="filter-section-inner provider-filter-group">
            <template v-if="arrivalAirportFilters.length">
              <label
                v-for="airport in arrivalAirportFilters"
                :key="airport"
                class="filter-toggle"
              >
                <input v-model="selectedArrivalAirports" :value="airport" type="checkbox" />
                <span>{{ airport }}</span>
              </label>
            </template>
            <p v-else class="filter-placeholder">Available after results load</p>
          </div>
        </div>
      </section>

      <section class="filter-section" :class="{ open: expandedSections.sources }">
        <button type="button" class="filter-section-summary" @click="toggleSection('sources')">Booking sources</button>
        <div class="filter-section-body" :class="{ open: expandedSections.sources }">
          <div class="filter-section-inner provider-filter-group">
            <template v-if="providerFilters.length">
              <label
                v-for="provider in providerFilters"
                :key="provider"
                class="filter-toggle"
              >
                <input v-model="selectedProviders" :value="provider" type="checkbox" />
                <span>{{ provider.replace('FlightApi:', '') }}</span>
              </label>
            </template>
            <p v-else class="filter-placeholder">Available after results load</p>
          </div>
        </div>
      </section>

      <section class="filter-section" :class="{ open: expandedSections.airlines }">
        <button type="button" class="filter-section-summary" @click="toggleSection('airlines')">Airlines</button>
        <div class="filter-section-body" :class="{ open: expandedSections.airlines }">
          <div class="filter-section-inner provider-filter-group">
            <template v-if="airlineFilters.length">
              <label
                v-for="airline in airlineFilters"
                :key="airline"
                class="filter-toggle"
              >
                <input v-model="selectedAirlines" :value="airline" type="checkbox" />
                <span>{{ airline }}</span>
              </label>
            </template>
            <p v-else class="filter-placeholder">Available after results load</p>
          </div>
        </div>
      </section>
    </div>
  </aside>
</template>

<style scoped src="./SearchFilters.css"></style>
