<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { AirportOption } from '../../features/flight-search/types'

const props = defineProps<{
  label: string
  inputAriaLabel: string
  suggestionsAriaLabel: string
  suggestionIdPrefix: string
  suggestions: AirportOption[]
  suggestionsLoading?: boolean
  suggestionsError?: string | null
  hasSearchedSuggestions?: boolean
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
const suggestionsOpen = computed(() => input.value.trim().length >= 2 && Boolean(
  props.suggestions.length > 0 || props.suggestionsLoading || props.suggestionsError || props.hasSearchedSuggestions
))

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
      <div class="airport-input-shell" :class="{ 'at-limit': atLimit }">
        <button
          v-for="airport in airports"
          :key="airport.code"
          type="button"
          class="airport-chip"
          :aria-label="`Remove ${airport.displayLabel} from ${label.toLowerCase()}`"
          :title="`Remove ${airport.displayLabel}`"
          @click="emit('removeAirport', airport.code)"
        >
          <span>{{ airport.code }}</span>
          <span class="airport-chip-remove" aria-hidden="true">×</span>
        </button>
        <input
          v-model="input"
          role="combobox"
          :aria-label="inputAriaLabel"
          aria-autocomplete="list"
          :aria-expanded="suggestionsOpen"
          :aria-controls="suggestionsId"
          :aria-activedescendant="activeIndex >= 0 ? `${suggestionIdPrefix}-suggestion-${suggestions[activeIndex]?.code}` : undefined"
          :disabled="atLimit"
          :placeholder="atLimit ? '' : 'Add airport or city'"
          @keydown="handleKeydown"
        />
      </div>
      <div v-if="suggestionsLoading" :id="suggestionsId" class="suggestions-list suggestions-status" role="status">Searching airports…</div>
      <div v-else-if="suggestionsError" :id="suggestionsId" class="suggestions-list suggestions-status suggestions-error" role="alert">{{ suggestionsError }}</div>
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
      <div v-else-if="hasSearchedSuggestions && !suggestionsLoading && !suggestionsError" :id="suggestionsId" class="suggestions-list suggestions-status" role="status">No matching airports found.</div>
    </div>
  </div>
</template>

<style scoped src="./AirportGroupPicker.css"></style>
