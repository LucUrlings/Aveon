<script setup lang="ts">
import { ref, watch } from 'vue'
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
const fromLabel = ref(props.modelValue.fromLabel)
const toLabel = ref(props.modelValue.toLabel)
const departureDate = ref(props.modelValue.departureDate)
const continuity = ref(props.modelValue.continuity)

watch([fromPicker.airports, toPicker.airports, fromLabel, toLabel, departureDate, continuity], () => {
  emit('update:modelValue', { ...props.modelValue, fromLabel: fromLabel.value.trim(), toLabel: toLabel.value.trim(), from: fromPicker.airports.value, to: toPicker.airports.value, departureDate: departureDate.value, continuity: continuity.value })
}, { deep: true })
</script>

<template>
  <fieldset class="ordered-leg">
    <legend>Flight {{ index + 1 }}</legend>
    <button v-if="removable" type="button" class="remove-leg" :aria-label="`Remove flight ${index + 1}`" @click="emit('remove')">Remove</button>
    <div class="leg-airports">
      <div><label class="group-name">From group name<input v-model="fromLabel" :aria-label="`Flight ${index + 1} departure group name`" required maxlength="80" /></label><AirportGroupPicker v-model:input="fromPicker.input.value" v-model:airports="fromPicker.airports.value" label="From airport group" :input-aria-label="`Flight ${index + 1}: add a departure airport or city`" :suggestions-aria-label="`Flight ${index + 1} departure airport suggestions`" :suggestion-id-prefix="`${modelValue.id}-from`" :suggestions="fromPicker.suggestions.value" :max-airports="maxAirports" @add-airport="fromPicker.addAirport" @remove-airport="fromPicker.removeAirport" @confirm-input="fromPicker.confirmInput" /></div>
      <div><label class="group-name">To group name<input v-model="toLabel" :aria-label="`Flight ${index + 1} arrival group name`" required maxlength="80" /></label><AirportGroupPicker v-model:input="toPicker.input.value" v-model:airports="toPicker.airports.value" label="To airport group" :input-aria-label="`Flight ${index + 1}: add an arrival airport or city`" :suggestions-aria-label="`Flight ${index + 1} arrival airport suggestions`" :suggestion-id-prefix="`${modelValue.id}-to`" :suggestions="toPicker.suggestions.value" :max-airports="maxAirports" @add-airport="toPicker.addAirport" @remove-airport="toPicker.removeAirport" @confirm-input="toPicker.confirmInput" /></div>
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
.group-name { display: grid; gap: 5px; margin-bottom: 10px; color: var(--muted); font-size: .9rem; }
.leg-airports { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; }
.leg-airports > div { min-width: 0; }
.leg-details { display: flex; gap: 16px; margin-top: 14px; }
.leg-details label { display: grid; gap: 6px; flex: 1; color: var(--muted); font-size: .9rem; }
input, select { width: 100%; box-sizing: border-box; padding: 10px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface); color: var(--ink-strong); }
@media (max-width: 680px) { .leg-airports { grid-template-columns: 1fr; } .leg-details { flex-direction: column; } }
</style>
