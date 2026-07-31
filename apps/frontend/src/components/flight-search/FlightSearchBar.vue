<script setup lang="ts">
import { ref, watch } from 'vue'
import DateRangePicker from './DateRangePicker.vue'
import type { AirportOption } from '../../features/flight-search/types'

const props = defineProps<{
  responseExists: boolean
  isCollapsed: boolean
  compactSummary: string
  searchCombinationCount: number
  maxDepartureRangeDays: number
  loading: boolean
  originSuggestions: AirportOption[]
  destinationSuggestions: AirportOption[]
  cabinOptions: Array<{ label: string; value: string }>
}>()

const originInput = defineModel<string>('originInput', { required: true })
const destinationInput = defineModel<string>('destinationInput', { required: true })
const originAirports = defineModel<AirportOption[]>('originAirports', { required: true })
const destinationAirports = defineModel<AirportOption[]>('destinationAirports', { required: true })
const tripType = defineModel<'oneWay' | 'return'>('tripType', { required: true })
const departureDateFrom = defineModel<string>('departureDateFrom', { required: true })
const departureDateTo = defineModel<string>('departureDateTo', { required: true })
const selectedDepartureDates = defineModel<string[]>('selectedDepartureDates', { required: true })
const returnDateFrom = defineModel<string | null>('returnDateFrom', { required: true })
const returnDateTo = defineModel<string | null>('returnDateTo', { required: true })
const selectedReturnDates = defineModel<string[]>('selectedReturnDates', { required: true })
const adults = defineModel<number>('adults', { required: true })
const cabinClass = defineModel<string>('cabinClass', { required: true })

const emit = defineEmits<{
  submit: []
  toggleCollapse: []
  swapLocations: []
  confirmOriginInput: []
  confirmDestinationInput: []
  removeOriginAirport: [code: string]
  removeDestinationAirport: [code: string]
  addOriginAirport: [airport: AirportOption]
  addDestinationAirport: [airport: AirportOption]
}>()

const originActiveIndex = ref(-1)
const destinationActiveIndex = ref(-1)

watch(() => props.originSuggestions, () => { originActiveIndex.value = -1 })
watch(() => props.destinationSuggestions, () => { destinationActiveIndex.value = -1 })

const handleSuggestionKeydown = (
  event: KeyboardEvent,
  suggestions: AirportOption[],
  activeIndex: number,
  setActiveIndex: (value: number) => void,
  addAirport: (airport: AirportOption) => void,
  confirmInput: () => void,
) => {
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    if (suggestions.length === 0) return
    const offset = event.key === 'ArrowDown' ? 1 : -1
    setActiveIndex((activeIndex + offset + suggestions.length) % suggestions.length)
    return
  }
  if (event.key === 'Escape') {
    setActiveIndex(-1)
    return
  }
  if (event.key !== 'Enter') return

  event.preventDefault()
  const suggestion = suggestions[activeIndex]
  if (suggestion) addAirport(suggestion)
  else confirmInput()
}
</script>

<template>
  <section class="search-shell" :class="{ collapsed: isCollapsed }">
    <div class="search-shell-header">
      <div>
        <p class="eyebrow">Search</p>
        <h2>{{ isCollapsed ? compactSummary : 'Build a flight search' }}</h2>
      </div>
      <button
        v-if="responseExists"
        type="button"
        class="collapse-toggle"
        aria-controls="flight-search-form"
        :aria-expanded="!isCollapsed"
        @click="emit('toggleCollapse')"
      >
        {{ isCollapsed ? 'Edit search' : 'Collapse' }}
      </button>
    </div>

    <Transition name="search-pane">
      <form v-if="!isCollapsed" id="flight-search-form" class="search-form" @submit.prevent="emit('submit')">
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
                  :aria-label="`Remove ${airport.displayLabel} from origin airports`"
                    @click="emit('removeOriginAirport', airport.code)"
                  >
                    {{ airport.code }}
                  </button>
                </div>
                <input
                  v-model="originInput"
                  role="combobox"
                  aria-label="Add an origin airport or city"
                  aria-autocomplete="list"
                  :aria-expanded="originSuggestions.length > 0"
                  aria-controls="origin-suggestions"
                  :aria-activedescendant="originActiveIndex >= 0 ? `origin-suggestion-${originSuggestions[originActiveIndex]?.code}` : undefined"
                  placeholder="Add airport or city"
                  @keydown="handleSuggestionKeydown($event, originSuggestions, originActiveIndex, (value) => originActiveIndex = value, (airport) => emit('addOriginAirport', airport), () => emit('confirmOriginInput'))"
                />
                <ul v-if="originSuggestions.length" id="origin-suggestions" class="suggestions-list" role="listbox" aria-label="Origin airport suggestions">
                  <li v-for="airport in originSuggestions" :key="airport.code" role="none">
                    <button
                      type="button"
                      class="suggestion-button"
                      role="option"
                      :id="`origin-suggestion-${airport.code}`"
                      tabindex="-1"
                      :aria-selected="originActiveIndex === originSuggestions.indexOf(airport)"
                      @mouseenter="originActiveIndex = originSuggestions.indexOf(airport)"
                      @click="emit('addOriginAirport', airport)"
                    >
                      {{ airport.displayLabel }}
                    </button>
                  </li>
                </ul>
              </div>
            </div>

            <div class="swap-locations-wrap">
              <button
                type="button"
                class="swap-locations-button"
                title="Swap origin and destination"
                aria-label="Swap origin and destination"
                @click="emit('swapLocations')"
              >
                <svg
                  class="swap-locations-icon"
                  viewBox="0 0 20 20"
                  fill="none"
                  xmlns="http://www.w3.org/2000/svg"
                  aria-hidden="true"
                >
                  <path
                    d="M3 6H14M14 6L11.5 3.5M14 6L11.5 8.5"
                    stroke="currentColor"
                    stroke-width="1.6"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                  />
                  <path
                    d="M17 14H6M6 14L8.5 11.5M6 14L8.5 16.5"
                    stroke="currentColor"
                    stroke-width="1.6"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                  />
                </svg>
              </button>
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
                  :aria-label="`Remove ${airport.displayLabel} from destination airports`"
                    @click="emit('removeDestinationAirport', airport.code)"
                  >
                    {{ airport.code }}
                  </button>
                </div>
                <input
                  v-model="destinationInput"
                  role="combobox"
                  aria-label="Add a destination airport or city"
                  aria-autocomplete="list"
                  :aria-expanded="destinationSuggestions.length > 0"
                  aria-controls="destination-suggestions"
                  :aria-activedescendant="destinationActiveIndex >= 0 ? `destination-suggestion-${destinationSuggestions[destinationActiveIndex]?.code}` : undefined"
                  placeholder="Add airport or city"
                  @keydown="handleSuggestionKeydown($event, destinationSuggestions, destinationActiveIndex, (value) => destinationActiveIndex = value, (airport) => emit('addDestinationAirport', airport), () => emit('confirmDestinationInput'))"
                />
                <ul v-if="destinationSuggestions.length" id="destination-suggestions" class="suggestions-list" role="listbox" aria-label="Destination airport suggestions">
                  <li v-for="airport in destinationSuggestions" :key="airport.code" role="none">
                    <button
                      type="button"
                      class="suggestion-button"
                      role="option"
                      :id="`destination-suggestion-${airport.code}`"
                      tabindex="-1"
                      :aria-selected="destinationActiveIndex === destinationSuggestions.indexOf(airport)"
                      @mouseenter="destinationActiveIndex = destinationSuggestions.indexOf(airport)"
                      @click="emit('addDestinationAirport', airport)"
                    >
                      {{ airport.displayLabel }}
                    </button>
                  </li>
                </ul>
              </div>
            </div>
          </div>

          <div class="settings-grid">
            <label class="field field-compact field-trip-type">
              <span>Trip type</span>
              <select v-model="tripType">
                <option value="oneWay">One way</option>
                <option value="return">Return</option>
              </select>
            </label>

            <div class="field field-wide">
              <span>Dates</span>
              <DateRangePicker
                v-model:start-date="departureDateFrom"
                v-model:end-date="departureDateTo"
                v-model:selected-dates="selectedDepartureDates"
                :max-range-days="maxDepartureRangeDays"
                heading="Select departure dates"
              />
            </div>

            <div v-if="tripType === 'return' && returnDateFrom && returnDateTo" class="field field-wide">
              <span>Return dates</span>
              <DateRangePicker
                v-model:start-date="returnDateFrom"
                v-model:end-date="returnDateTo"
                v-model:selected-dates="selectedReturnDates"
                :max-range-days="maxDepartureRangeDays"
                heading="Select return dates"
              />
            </div>

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

        <div class="search-actions">
          <p class="combination-count" aria-live="polite">
            {{ searchCombinationCount }}
            {{ searchCombinationCount === 1 ? 'combination' : 'combinations' }}
          </p>

          <button class="search-button" type="submit" :disabled="loading" :aria-busy="loading">
            {{ loading ? 'Searching...' : 'Search flights' }}
          </button>
        </div>
      </form>
    </Transition>
  </section>
</template>

<style scoped src="./FlightSearchBar.css"></style>
