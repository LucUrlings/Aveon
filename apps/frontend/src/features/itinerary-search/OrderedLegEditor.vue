<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import AirportGroupPicker from '../../components/flight-search/AirportGroupPicker.vue'
import { useAirportPicker } from '../flight-search/useAirportPicker'
import type { AirportOption } from '../flight-search/types'

export type OrderedLegModel = {
  id: string
  fromLabel: string
  toLabel: string
  from: AirportOption[]
  to: AirportOption[]
  departureDate: string
  continuity: 'sameAirport' | 'allowSwitch'
}

const props = defineProps<{ modelValue: OrderedLegModel; index: number; removable: boolean; maxAirports: number }>()
const emit = defineEmits<{ 'update:modelValue': [value: OrderedLegModel]; remove: [] }>()
const fromPicker = useAirportPicker(props.modelValue.from)
const toPicker = useAirportPicker(props.modelValue.to)
const departureDate = ref(props.modelValue.departureDate)
const continuity = ref(props.modelValue.continuity)
const airportGroupName = (airports: AirportOption[], fallback: string) => airports.length
  ? airports.map(airport => airport.name ? `${airport.name} (${airport.code})` : airport.code).join(' / ').slice(0, 80)
  : fallback
const connectedFrom = computed(() => airportGroupName(props.modelValue.from, 'the previous destination'))
const destinationName = computed(() => airportGroupName(toPicker.airports.value, 'Next destination'))

watch([fromPicker.airports, toPicker.airports, departureDate, continuity], () => {
  const from = props.index === 0 ? fromPicker.airports.value : props.modelValue.from
  const to = toPicker.airports.value
  emit('update:modelValue', {
    ...props.modelValue,
    fromLabel: airportGroupName(from, 'Starting airport group'),
    toLabel: airportGroupName(to, `Destination ${props.index + 1}`),
    from,
    to,
    departureDate: departureDate.value,
    continuity: continuity.value,
  })
}, { deep: true })
</script>

<template>
  <fieldset class="ordered-leg">
    <legend>{{ index === 0 ? 'Start your route' : destinationName }}</legend>
    <button v-if="removable" type="button" class="remove-leg" :aria-label="`Remove destination ${index + 1}`" @click="emit('remove')">Remove</button>
    <div class="leg-airports">
      <AirportGroupPicker v-if="index === 0" v-model:input="fromPicker.input.value" v-model:airports="fromPicker.airports.value" label="Starting airport group" input-aria-label="Add a starting airport or city" suggestions-aria-label="Starting airport suggestions" :suggestion-id-prefix="`${modelValue.id}-from`" :suggestions="fromPicker.suggestions.value" :suggestions-loading="fromPicker.suggestionsLoading.value" :suggestions-error="fromPicker.suggestionsError.value" :has-searched-suggestions="fromPicker.hasSearchedSuggestions.value" :max-airports="maxAirports" @add-airport="fromPicker.addAirport" @remove-airport="fromPicker.removeAirport" @confirm-input="fromPicker.confirmInput" />
      <div v-else class="connected-from" aria-live="polite"><span>Continues from</span><strong>{{ connectedFrom }}</strong><small>Edit the preceding destination to change this group.</small></div>
      <div class="route-arrow" aria-hidden="true">→</div>
      <AirportGroupPicker v-model:input="toPicker.input.value" v-model:airports="toPicker.airports.value" :label="index === 0 ? 'First destination' : 'Next destination'" :input-aria-label="`Add airports for destination ${index + 1}`" :suggestions-aria-label="`Destination ${index + 1} airport suggestions`" :suggestion-id-prefix="`${modelValue.id}-to`" :suggestions="toPicker.suggestions.value" :suggestions-loading="toPicker.suggestionsLoading.value" :suggestions-error="toPicker.suggestionsError.value" :has-searched-suggestions="toPicker.hasSearchedSuggestions.value" :max-airports="maxAirports" @add-airport="toPicker.addAirport" @remove-airport="toPicker.removeAirport" @confirm-input="toPicker.confirmInput" />
    </div>
    <div class="leg-details">
      <label>Departure date<input v-model="departureDate" type="date" required /></label>
      <label v-if="index > 0">Connection airport
        <select v-model="continuity">
          <option value="sameAirport">Continue from the same airport</option>
          <option value="allowSwitch">Allow an airport change</option>
        </select>
      </label>
    </div>
  </fieldset>
</template>

<style scoped>
.ordered-leg { position: relative; min-width: 0; margin: 0; padding: 18px; border: 1px solid var(--border); border-radius: var(--radius-md); }
legend { padding: 0 8px; font-weight: 700; color: var(--ink-strong); }
.remove-leg { position: absolute; top: 10px; right: 12px; border: 0; background: transparent; color: var(--muted); cursor: pointer; }
.leg-airports { display: grid; grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr); gap: 14px; align-items: center; }
.leg-airports > div { min-width: 0; }
.connected-from { display: grid; gap: 4px; padding: 12px 14px; border: 1px solid var(--border); border-radius: 10px; background: var(--brand-soft); }
.connected-from span, .connected-from small { color: var(--muted); font-size: .8rem; }
.connected-from strong { overflow-wrap: anywhere; color: var(--ink-strong); }
.route-arrow { color: var(--brand-strong); font-size: 1.4rem; font-weight: 800; }
.leg-details { display: flex; gap: 16px; margin-top: 14px; }
.leg-details label { display: grid; gap: 6px; flex: 1; color: var(--muted); font-size: .9rem; }
input, select { width: 100%; box-sizing: border-box; padding: 10px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface); color: var(--ink-strong); }
@media (max-width: 680px) { .leg-airports { grid-template-columns: 1fr; } .route-arrow { transform: rotate(90deg); justify-self: center; } .leg-details { flex-direction: column; } }
</style>
