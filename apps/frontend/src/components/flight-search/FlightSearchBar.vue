<script setup lang="ts">
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
const departureDateFrom = defineModel<string>('departureDateFrom', { required: true })
const departureDateTo = defineModel<string>('departureDateTo', { required: true })
const selectedDepartureDates = defineModel<string[]>('selectedDepartureDates', { required: true })
const adults = defineModel<number>('adults', { required: true })
const cabinClass = defineModel<string>('cabinClass', { required: true })

const emit = defineEmits<{
  submit: []
  toggleCollapse: []
  confirmOriginInput: []
  confirmDestinationInput: []
  removeOriginAirport: [code: string]
  removeDestinationAirport: [code: string]
  addOriginAirport: [airport: AirportOption]
  addDestinationAirport: [airport: AirportOption]
}>()
</script>

<template>
  <section class="search-shell" :class="{ collapsed: isCollapsed }">
    <div class="search-shell-header">
      <div>
        <p class="eyebrow">Search</p>
        <h2>{{ isCollapsed ? compactSummary : 'Build a one-way search' }}</h2>
      </div>
      <button
        v-if="responseExists"
        type="button"
        class="collapse-toggle"
        @click="emit('toggleCollapse')"
      >
        {{ isCollapsed ? 'Edit search' : 'Collapse' }}
      </button>
    </div>

    <Transition name="search-pane">
      <form v-if="!isCollapsed" class="search-form" @submit.prevent="emit('submit')">
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
                    @click="emit('removeOriginAirport', airport.code)"
                  >
                    {{ airport.code }}
                  </button>
                </div>
                <input
                  v-model="originInput"
                  placeholder="Add airport or city"
                  @keydown.enter.prevent="emit('confirmOriginInput')"
                />
                <ul v-if="originSuggestions.length" class="suggestions-list">
                  <li v-for="airport in originSuggestions" :key="airport.code">
                    <button
                      type="button"
                      class="suggestion-button"
                      @click="emit('addOriginAirport', airport)"
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
                    @click="emit('removeDestinationAirport', airport.code)"
                  >
                    {{ airport.code }}
                  </button>
                </div>
                <input
                  v-model="destinationInput"
                  placeholder="Add airport or city"
                  @keydown.enter.prevent="emit('confirmDestinationInput')"
                />
                <ul v-if="destinationSuggestions.length" class="suggestions-list">
                  <li v-for="airport in destinationSuggestions" :key="airport.code">
                    <button
                      type="button"
                      class="suggestion-button"
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
            <label class="field">
              <span>Dates</span>
              <DateRangePicker
                v-model:start-date="departureDateFrom"
                v-model:end-date="departureDateTo"
                v-model:selected-dates="selectedDepartureDates"
                :max-range-days="maxDepartureRangeDays"
              />
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

        <div class="search-actions">
          <p class="combination-count">
            {{ searchCombinationCount }}
            {{ searchCombinationCount === 1 ? 'combination' : 'combinations' }}
          </p>

          <button class="search-button" type="submit" :disabled="loading">
            {{ loading ? 'Searching...' : 'Search flights' }}
          </button>
        </div>
      </form>
    </Transition>
  </section>
</template>

<style scoped src="./FlightSearchBar.css"></style>
