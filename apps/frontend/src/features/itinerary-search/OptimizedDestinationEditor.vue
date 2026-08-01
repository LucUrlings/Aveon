<script setup lang="ts">
import { ref, watch } from 'vue'
import AirportGroupPicker from '../../components/flight-search/AirportGroupPicker.vue'
import { useAirportPicker } from '../flight-search/useAirportPicker'
import type { AirportOption } from '../flight-search/types'

export type OptimizedDestinationModel = {
  id: string
  label: string
  airports: AirportOption[]
  stayMode: 'minimumNights' | 'exactNights'
  nights: number
  continuity: 'inherit' | 'sameAirport' | 'allowSwitch'
}

const props = defineProps<{ modelValue: OptimizedDestinationModel; index: number; removable: boolean; maxAirports: number }>()
const emit = defineEmits<{ 'update:modelValue': [value: OptimizedDestinationModel]; remove: [] }>()
const picker = useAirportPicker(props.modelValue.airports)
const label = ref(props.modelValue.label)
const stayMode = ref(props.modelValue.stayMode)
const nights = ref(props.modelValue.nights)
const continuity = ref(props.modelValue.continuity)

watch([picker.airports, label, stayMode, nights, continuity], () => {
  emit('update:modelValue', {
    ...props.modelValue,
    label: label.value.trim(),
    airports: picker.airports.value,
    stayMode: stayMode.value,
    nights: Math.max(0, Number(nights.value) || 0),
    continuity: continuity.value,
  })
}, { deep: true })
</script>

<template>
  <fieldset class="optimized-destination">
    <legend>Destination {{ index + 1 }}</legend>
    <button v-if="removable" type="button" class="remove-destination" :aria-label="`Remove destination ${index + 1}`" @click="emit('remove')">Remove</button>
    <label class="group-name">Group name<input v-model="label" :aria-label="`Destination ${index + 1} group name`" required maxlength="80" /></label>
    <AirportGroupPicker v-model:input="picker.input.value" v-model:airports="picker.airports.value" label="Destination airport group" :input-aria-label="`Destination ${index + 1}: add an airport or city`" :suggestions-aria-label="`Destination ${index + 1} airport suggestions`" :suggestion-id-prefix="`${modelValue.id}-airports`" :suggestions="picker.suggestions.value" :max-airports="maxAirports" @add-airport="picker.addAirport" @remove-airport="picker.removeAirport" @confirm-input="picker.confirmInput" />
    <div class="destination-rules">
      <label>Stay rule
        <select v-model="stayMode" :aria-label="`Destination ${index + 1} stay rule`">
          <option value="minimumNights">At least</option>
          <option value="exactNights">Exactly</option>
        </select>
      </label>
      <label>Nights<input v-model.number="nights" :aria-label="`Destination ${index + 1} nights`" type="number" min="0" max="30" required /></label>
      <label>Airport continuity
        <select v-model="continuity" :aria-label="`Destination ${index + 1} airport continuity`">
          <option value="inherit">Use trip default</option>
          <option value="sameAirport">Same airport</option>
          <option value="allowSwitch">Allow airport change</option>
        </select>
      </label>
    </div>
  </fieldset>
</template>

<style scoped>
.optimized-destination { position: relative; min-width: 0; margin: 0; padding: 18px; border: 1px solid var(--border); border-radius: var(--radius-md); }
legend { padding: 0 8px; font-weight: 700; color: var(--ink-strong); }
.remove-destination { position: absolute; top: 10px; right: 12px; border: 0; background: transparent; color: var(--muted); cursor: pointer; }
.group-name { display: grid; gap: 5px; margin-bottom: 12px; color: var(--muted); font-size: .9rem; }
.destination-rules { display: grid; grid-template-columns: 1fr .65fr 1.3fr; gap: 12px; margin-top: 14px; }
.destination-rules label { display: grid; gap: 5px; color: var(--muted); font-size: .9rem; }
input, select { width: 100%; box-sizing: border-box; padding: 9px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface); color: var(--ink-strong); }
@media (max-width: 680px) { .destination-rules { grid-template-columns: 1fr; } }
</style>
