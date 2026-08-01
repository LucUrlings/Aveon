<script setup lang="ts">
import { ref } from 'vue'

const props = defineProps<{
  tripType: 'oneWay' | 'return'
  selectedOutboundLegId: string | null
  providerFilters: string[]
  airlineFilters: string[]
  departureAirportFilters: string[]
  arrivalAirportFilters: string[]
  availableMaxDurationMinutes: number
}>()

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
const returnDepartureTimeRange = defineModel<[number, number]>('returnDepartureTimeRange', { required: true })
const returnArrivalTimeRange = defineModel<[number, number]>('returnArrivalTimeRange', { required: true })

const expandedSections = ref({
  duration: true,
  stops: true,
  departure: true,
  arrival: true,
  returnDeparture: true,
  returnArrival: true,
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

const toggleSection = (section: keyof typeof expandedSections.value) => {
  expandedSections.value[section] = !expandedSections.value[section]
}
</script>

<template>
  <aside class="filters-panel" aria-labelledby="search-filters-title">
    <div class="filters-card">
      <p class="eyebrow">Filters</p>
      <h3 id="search-filters-title">{{ props.selectedOutboundLegId ? 'Refine return options' : 'Refine results' }}</h3>

      <section class="filter-section" :class="{ open: expandedSections.stops }">
        <button type="button" class="filter-section-summary" :aria-expanded="expandedSections.stops" aria-controls="filter-stops" @click="toggleSection('stops')">{{ props.selectedOutboundLegId ? 'Return stops' : 'Stops' }}</button>
        <div v-show="expandedSections.stops" id="filter-stops" class="filter-section-body" :class="{ open: expandedSections.stops }">
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
        <button type="button" class="filter-section-summary" :aria-expanded="expandedSections.duration" aria-controls="filter-duration" @click="toggleSection('duration')">{{ props.selectedOutboundLegId ? 'Max return duration' : 'Max duration' }}</button>
        <div v-show="expandedSections.duration" id="filter-duration" class="filter-section-body" :class="{ open: expandedSections.duration }">
          <div class="filter-section-inner time-filter-group">
            <div class="time-filter-header">
              <span class="filter-label">{{ props.selectedOutboundLegId ? 'Max return duration' : 'Max duration' }}</span>
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
                :aria-label="props.selectedOutboundLegId ? 'Maximum return flight duration' : 'Maximum journey duration'"
                min="0"
                :max="availableMaxDurationMinutes"
                step="15"
                @input="maxDurationMinutes = Number(($event.target as HTMLInputElement).value)"
              />
            </div>
          </div>
        </div>
      </section>

      <section v-if="!props.selectedOutboundLegId" class="filter-section" :class="{ open: expandedSections.departure }">
        <button type="button" class="filter-section-summary" :aria-expanded="expandedSections.departure" aria-controls="filter-departure" @click="toggleSection('departure')">
          {{ props.tripType === 'return' ? 'Outbound departure time' : 'Departure time' }}
        </button>
        <div v-show="expandedSections.departure" id="filter-departure" class="filter-section-body" :class="{ open: expandedSections.departure }">
          <div class="filter-section-inner time-filter-group">
            <div class="time-filter-header">
              <span class="filter-label">{{ props.tripType === 'return' ? 'Outbound departure time' : 'Departure time' }}</span>
              <strong>{{ formatMinutes(departureTimeRange[0]) }} - {{ formatMinutes(departureTimeRange[1]) }}</strong>
            </div>
            <div class="range-slider">
              <div class="range-slider-track" />
              <div class="range-slider-selected" :style="getRangeStyle(departureTimeRange)" />
              <input
                :value="departureTimeRange[0]"
                type="range"
                :aria-label="`${props.tripType === 'return' ? 'Outbound' : 'Flight'} departure time from`"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(departureTimeRange, 0, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
              <input
                :value="departureTimeRange[1]"
                type="range"
                :aria-label="`${props.tripType === 'return' ? 'Outbound' : 'Flight'} departure time to`"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(departureTimeRange, 1, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
            </div>
          </div>
        </div>
      </section>

      <section v-if="!props.selectedOutboundLegId" class="filter-section" :class="{ open: expandedSections.arrival }">
        <button type="button" class="filter-section-summary" :aria-expanded="expandedSections.arrival" aria-controls="filter-arrival" @click="toggleSection('arrival')">
          {{ props.tripType === 'return' ? 'Outbound arrival time' : 'Arrival time' }}
        </button>
        <div v-show="expandedSections.arrival" id="filter-arrival" class="filter-section-body" :class="{ open: expandedSections.arrival }">
          <div class="filter-section-inner time-filter-group">
            <div class="time-filter-header">
              <span class="filter-label">{{ props.tripType === 'return' ? 'Outbound arrival time' : 'Arrival time' }}</span>
              <strong>{{ formatMinutes(arrivalTimeRange[0]) }} - {{ formatMinutes(arrivalTimeRange[1]) }}</strong>
            </div>
            <div class="range-slider">
              <div class="range-slider-track" />
              <div class="range-slider-selected" :style="getRangeStyle(arrivalTimeRange)" />
              <input
                :value="arrivalTimeRange[0]"
                type="range"
                :aria-label="`${props.tripType === 'return' ? 'Outbound' : 'Flight'} arrival time from`"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(arrivalTimeRange, 0, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
              <input
                :value="arrivalTimeRange[1]"
                type="range"
                :aria-label="`${props.tripType === 'return' ? 'Outbound' : 'Flight'} arrival time to`"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(arrivalTimeRange, 1, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
            </div>
          </div>
        </div>
      </section>

      <section
        v-if="props.tripType === 'return'"
        class="filter-section"
        :class="{ open: expandedSections.returnDeparture }"
      >
        <button type="button" class="filter-section-summary" :aria-expanded="expandedSections.returnDeparture" aria-controls="filter-return-departure" @click="toggleSection('returnDeparture')">{{ props.selectedOutboundLegId ? 'Departure time' : 'Return departure time' }}</button>
        <div v-show="expandedSections.returnDeparture" id="filter-return-departure" class="filter-section-body" :class="{ open: expandedSections.returnDeparture }">
          <div class="filter-section-inner time-filter-group">
            <div class="time-filter-header">
              <span class="filter-label">{{ props.selectedOutboundLegId ? 'Departure time' : 'Return departure time' }}</span>
              <strong>{{ formatMinutes(returnDepartureTimeRange[0]) }} - {{ formatMinutes(returnDepartureTimeRange[1]) }}</strong>
            </div>
            <div class="range-slider">
              <div class="range-slider-track" />
              <div class="range-slider-selected" :style="getRangeStyle(returnDepartureTimeRange)" />
              <input
                :value="returnDepartureTimeRange[0]"
                type="range"
                aria-label="Return departure time from"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(returnDepartureTimeRange, 0, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
              <input
                :value="returnDepartureTimeRange[1]"
                type="range"
                aria-label="Return departure time to"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(returnDepartureTimeRange, 1, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
            </div>
          </div>
        </div>
      </section>

      <section
        v-if="props.tripType === 'return'"
        class="filter-section"
        :class="{ open: expandedSections.returnArrival }"
      >
        <button type="button" class="filter-section-summary" :aria-expanded="expandedSections.returnArrival" aria-controls="filter-return-arrival" @click="toggleSection('returnArrival')">{{ props.selectedOutboundLegId ? 'Arrival time' : 'Return arrival time' }}</button>
        <div v-show="expandedSections.returnArrival" id="filter-return-arrival" class="filter-section-body" :class="{ open: expandedSections.returnArrival }">
          <div class="filter-section-inner time-filter-group">
            <div class="time-filter-header">
              <span class="filter-label">{{ props.selectedOutboundLegId ? 'Arrival time' : 'Return arrival time' }}</span>
              <strong>{{ formatMinutes(returnArrivalTimeRange[0]) }} - {{ formatMinutes(returnArrivalTimeRange[1]) }}</strong>
            </div>
            <div class="range-slider">
              <div class="range-slider-track" />
              <div class="range-slider-selected" :style="getRangeStyle(returnArrivalTimeRange)" />
              <input
                :value="returnArrivalTimeRange[0]"
                type="range"
                aria-label="Return arrival time from"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(returnArrivalTimeRange, 0, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
              <input
                :value="returnArrivalTimeRange[1]"
                type="range"
                aria-label="Return arrival time to"
                min="0"
                max="1440"
                step="15"
                @input="ensureOrderedRange(returnArrivalTimeRange, 1, Number(($event.target as HTMLInputElement).value), 0, 1440)"
              />
            </div>
          </div>
        </div>
      </section>

      <section class="filter-section" :class="{ open: expandedSections.departureAirports }">
        <button type="button" class="filter-section-summary" :aria-expanded="expandedSections.departureAirports" aria-controls="filter-departure-airports" @click="toggleSection('departureAirports')">{{ props.selectedOutboundLegId ? 'Return departure airport' : 'Departure airport' }}</button>
        <div v-show="expandedSections.departureAirports" id="filter-departure-airports" class="filter-section-body" :class="{ open: expandedSections.departureAirports }">
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
        <button type="button" class="filter-section-summary" :aria-expanded="expandedSections.arrivalAirports" aria-controls="filter-arrival-airports" @click="toggleSection('arrivalAirports')">{{ props.selectedOutboundLegId ? 'Return arrival airport' : 'Arrival airport' }}</button>
        <div v-show="expandedSections.arrivalAirports" id="filter-arrival-airports" class="filter-section-body" :class="{ open: expandedSections.arrivalAirports }">
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
        <button type="button" class="filter-section-summary" :aria-expanded="expandedSections.sources" aria-controls="filter-sources" @click="toggleSection('sources')">Booking sources</button>
        <div v-show="expandedSections.sources" id="filter-sources" class="filter-section-body" :class="{ open: expandedSections.sources }">
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
        <button type="button" class="filter-section-summary" :aria-expanded="expandedSections.airlines" aria-controls="filter-airlines" @click="toggleSection('airlines')">{{ props.selectedOutboundLegId ? 'Return airlines' : 'Airlines' }}</button>
        <div v-show="expandedSections.airlines" id="filter-airlines" class="filter-section-body" :class="{ open: expandedSections.airlines }">
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
