<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { AirportOption } from '../../features/flight-search/types'

const props = defineProps<{
  label: string
  inputAriaLabel: string
  suggestionsAriaLabel: string
  suggestionIdPrefix: string
  suggestions: AirportOption[]
  maxAirports?: number
}>()

const input = defineModel<string>('input', { required: true })
const airports = defineModel<AirportOption[]>('airports', { required: true })

const emit = defineEmits<{
  confirmInput: []
  removeAirport: [code: string]
  addAirport: [airport: AirportOption]
}>()

const activeIndex = ref(-1)
const suggestionsId = `${props.suggestionIdPrefix}-suggestions`
const atLimit = computed(() => props.maxAirports !== undefined && airports.value.length >= props.maxAirports)

const addAirport = (airport: AirportOption) => {
  if (!atLimit.value) emit('addAirport', airport)
}

watch(() => props.suggestions, () => { activeIndex.value = -1 })

const handleKeydown = (event: KeyboardEvent) => {
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    if (props.suggestions.length === 0) return
    const offset = event.key === 'ArrowDown' ? 1 : -1
    activeIndex.value = (activeIndex.value + offset + props.suggestions.length) % props.suggestions.length
    return
  }

  if (event.key === 'Escape') {
    activeIndex.value = -1
    return
  }

  if (event.key !== 'Enter') return

  event.preventDefault()
  const suggestion = props.suggestions[activeIndex.value]
  if (suggestion) addAirport(suggestion)
  else emit('confirmInput')
}
</script>

<template>
  <div class="field airport-group-field">
    <span>{{ label }}</span>
    <small v-if="maxAirports" class="airport-limit">Up to {{ maxAirports }} airport{{ maxAirports === 1 ? '' : 's' }}</small>
    <div class="airport-picker">
      <div class="chip-row">
        <button
          v-for="airport in airports"
          :key="airport.code"
          type="button"
          class="airport-chip"
          :aria-label="`Remove ${airport.displayLabel} from ${label.toLowerCase()}`"
          @click="emit('removeAirport', airport.code)"
        >
          {{ airport.code }}
        </button>
      </div>
      <input
        v-model="input"
        role="combobox"
        :aria-label="inputAriaLabel"
        aria-autocomplete="list"
        :aria-expanded="suggestions.length > 0"
        :aria-controls="suggestionsId"
        :aria-activedescendant="activeIndex >= 0 ? `${suggestionIdPrefix}-suggestion-${suggestions[activeIndex]?.code}` : undefined"
        :disabled="atLimit"
        placeholder="Add airport or city"
        @keydown="handleKeydown"
      />
      <ul v-if="suggestions.length" :id="suggestionsId" class="suggestions-list" role="listbox" :aria-label="suggestionsAriaLabel">
        <li v-for="airport in suggestions" :key="airport.code" role="none">
          <button
            type="button"
            class="suggestion-button"
            role="option"
            :id="`${suggestionIdPrefix}-suggestion-${airport.code}`"
            tabindex="-1"
            :aria-selected="activeIndex === suggestions.indexOf(airport)"
            @mouseenter="activeIndex = suggestions.indexOf(airport)"
            @click="addAirport(airport)"
          >
            {{ airport.displayLabel }}
          </button>
        </li>
      </ul>
    </div>
  </div>
</template>

<style scoped src="./AirportGroupPicker.css"></style>
