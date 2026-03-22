<script setup lang="ts">
import type { AirportOption } from '../../features/flight-search/types'

defineProps<{
  responseExists: boolean
  isCollapsed: boolean
  compactSummary: string
  loading: boolean
  originSuggestions: AirportOption[]
  destinationSuggestions: AirportOption[]
  cabinOptions: Array<{ label: string; value: string }>
  flexibilityOptions: Array<{ label: string; value: number }>
}>()

const originInput = defineModel<string>('originInput', { required: true })
const destinationInput = defineModel<string>('destinationInput', { required: true })
const originAirports = defineModel<AirportOption[]>('originAirports', { required: true })
const destinationAirports = defineModel<AirportOption[]>('destinationAirports', { required: true })
const departureDate = defineModel<string>('departureDate', { required: true })
const flexibleDays = defineModel<number>('flexibleDays', { required: true })
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
    </Transition>
  </section>
</template>

<style scoped>
.search-shell {
  border: 1px solid rgba(29, 34, 40, 0.08);
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.88);
  box-shadow: 0 24px 60px rgba(41, 49, 61, 0.08);
  backdrop-filter: blur(18px);
  padding: 12px 14px;
  margin-bottom: 10px;
}

.search-shell.collapsed {
  padding-bottom: 12px;
}

.search-shell-header {
  display: flex;
  justify-content: space-between;
  align-items: start;
  gap: 10px;
  margin-bottom: 6px;
}

.eyebrow {
  margin: 0 0 6px;
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: #9c5a11;
}

h2 {
  margin: 0;
  color: #1d2228;
  font-size: 1.1rem;
  font-weight: 600;
}

.collapse-toggle {
  border: 1px solid rgba(29, 34, 40, 0.12);
  border-radius: 999px;
  padding: 6px 10px;
  background: #fff;
  font: inherit;
  font-weight: 600;
  cursor: pointer;
}

.search-form {
  margin-top: 6px;
}

.search-layout {
  display: grid;
  gap: 10px;
}

.airport-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.settings-grid {
  display: grid;
  grid-template-columns: minmax(220px, 0.8fr) minmax(170px, 0.55fr) minmax(120px, 0.3fr) minmax(180px, 0.45fr);
  gap: 8px;
  max-width: 980px;
}

.field {
  display: grid;
  gap: 6px;
}

.field span {
  font-size: 0.74rem;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: #5b6570;
}

.airport-picker {
  position: relative;
  display: grid;
  gap: 8px;
}

.chip-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  min-height: 30px;
}

.airport-chip {
  border: none;
  border-radius: 999px;
  padding: 4px 8px;
  background: #eef4fb;
  color: #1f5fbf;
  font-weight: 600;
  font-size: 0.82rem;
  cursor: pointer;
}

.airport-picker input,
.field input,
.field select {
  width: 100%;
  box-sizing: border-box;
  border: 1px solid #d5dbe1;
  border-radius: 10px;
  padding: 8px 10px;
  font: inherit;
  font-size: 0.92rem;
  background: #fff;
  color: #1d2228;
}

.airport-picker input:focus,
.field input:focus,
.field select:focus {
  outline: none;
  border-color: #2c7be5;
  box-shadow: 0 0 0 3px rgba(44, 123, 229, 0.14);
}

.field-compact {
  max-width: 110px;
}

.suggestions-list {
  list-style: none;
  padding: 6px;
  margin: 0;
  border: 1px solid rgba(29, 34, 40, 0.08);
  border-radius: 10px;
  background: #fff;
  box-shadow: 0 20px 45px rgba(41, 49, 61, 0.12);
}

.suggestion-button {
  width: 100%;
  border: none;
  background: transparent;
  text-align: left;
  padding: 6px 8px;
  border-radius: 8px;
  cursor: pointer;
  font-size: 0.9rem;
}

.suggestion-button:hover {
  background: #f2f5f8;
}

.search-button {
  margin-top: 12px;
  border: none;
  border-radius: 999px;
  padding: 8px 14px;
  font: inherit;
  font-weight: 600;
  color: #fff;
  background: linear-gradient(135deg, #1f5fbf 0%, #2c7be5 100%);
  cursor: pointer;
}

.search-button:disabled {
  opacity: 0.7;
  cursor: wait;
}

.search-pane-enter-active,
.search-pane-leave-active {
  transition:
    opacity 0.24s ease,
    transform 0.24s ease,
    max-height 0.24s ease;
  transform-origin: top;
  overflow: hidden;
}

.search-pane-enter-from,
.search-pane-leave-to {
  opacity: 0;
  transform: translateY(-6px);
  max-height: 0;
}

.search-pane-enter-to,
.search-pane-leave-from {
  opacity: 1;
  transform: translateY(0);
  max-height: 480px;
}

@media (max-width: 960px) {
  .airport-grid,
  .settings-grid {
    grid-template-columns: 1fr;
  }

  .field-compact {
    max-width: none;
  }
}
</style>
