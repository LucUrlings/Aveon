<script setup lang="ts">
import type { ItineraryFilters, ItineraryResultsQuery, Ranking } from './types'

defineProps<{ filters?: ItineraryFilters | null }>()
const query = defineModel<ItineraryResultsQuery>({ required: true })
const setList = (field: 'airlines' | 'bookingSources' | 'departureAirports' | 'arrivalAirports', value: string, checked: boolean) => {
  const values = new Set(query.value[field] ?? [])
  checked ? values.add(value) : values.delete(value)
  query.value = { ...query.value, [field]: [...values], page: 1 }
}
const set = <K extends keyof ItineraryResultsQuery>(field: K, value: ItineraryResultsQuery[K]) => { query.value = { ...query.value, [field]: value, page: 1 } }
const setTime = (field: 'departureTime' | 'arrivalTime', index: 0 | 1, raw: string) => {
  const [hours, minutes] = raw.split(':').map(Number)
  const range: [number, number] = [...(query.value[field] ?? [0, 1439])]
  range[index] = hours * 60 + minutes
  set(field, range)
}
</script>

<template>
  <aside class="filters" aria-label="Itinerary filters">
    <h2>Filter complete trips</h2>
    <label>Sort by
      <select :value="query.ranking ?? 'recommended'" @change="set('ranking', ($event.target as HTMLSelectElement).value as Ranking)">
        <option value="recommended">Recommended</option><option value="cheapest">Cheapest</option><option value="fastest">Fastest</option>
      </select>
    </label>
    <fieldset><legend>Stops on every flight</legend>
      <label><input type="checkbox" :checked="query.direct" @change="set('direct', ($event.target as HTMLInputElement).checked || undefined)" /> Direct</label>
      <label><input type="checkbox" :checked="query.oneStop" @change="set('oneStop', ($event.target as HTMLInputElement).checked || undefined)" /> 1 stop</label>
      <label><input type="checkbox" :checked="query.twoPlusStops" @change="set('twoPlusStops', ($event.target as HTMLInputElement).checked || undefined)" /> 2+ stops</label>
    </fieldset>
    <fieldset v-if="filters?.airlines.length"><legend>Airlines on every flight</legend>
      <label v-for="option in filters.airlines" :key="option.value"><input type="checkbox" :checked="query.airlines?.includes(option.value)" @change="setList('airlines', option.value, ($event.target as HTMLInputElement).checked)" /> {{ option.label }} ({{ option.count }})</label>
    </fieldset>
    <fieldset v-if="filters?.bookingSources.length"><legend>Booking sources</legend>
      <label v-for="option in filters.bookingSources" :key="option.value"><input type="checkbox" :checked="query.bookingSources?.includes(option.value)" @change="setList('bookingSources', option.value, ($event.target as HTMLInputElement).checked)" /> {{ option.label }} ({{ option.count }})</label>
    </fieldset>
    <fieldset v-if="filters?.departureAirports.length"><legend>First departure airport</legend>
      <label v-for="option in filters.departureAirports" :key="option.value"><input type="checkbox" :checked="query.departureAirports?.includes(option.value)" @change="setList('departureAirports', option.value, ($event.target as HTMLInputElement).checked)" /> {{ option.label }} ({{ option.count }})</label>
    </fieldset>
    <fieldset v-if="filters?.arrivalAirports.length"><legend>Final arrival airport</legend>
      <label v-for="option in filters.arrivalAirports" :key="option.value"><input type="checkbox" :checked="query.arrivalAirports?.includes(option.value)" @change="setList('arrivalAirports', option.value, ($event.target as HTMLInputElement).checked)" /> {{ option.label }} ({{ option.count }})</label>
    </fieldset>
    <label>Maximum total price<input type="number" min="0" :max="filters?.maxPrice ?? undefined" :value="query.maxPrice" @change="set('maxPrice', Number(($event.target as HTMLInputElement).value) || undefined)" /></label>
    <label>Maximum in-air duration (minutes)<input type="number" min="1" :max="filters?.maxDurationMinutes ?? undefined" :value="query.maxDurationMinutes" @change="set('maxDurationMinutes', Number(($event.target as HTMLInputElement).value) || undefined)" /></label>
    <fieldset><legend>First departure time</legend><div class="time-range"><input aria-label="Earliest departure time" type="time" @change="setTime('departureTime', 0, ($event.target as HTMLInputElement).value)" /><input aria-label="Latest departure time" type="time" @change="setTime('departureTime', 1, ($event.target as HTMLInputElement).value)" /></div></fieldset>
    <fieldset><legend>Final arrival time</legend><div class="time-range"><input aria-label="Earliest arrival time" type="time" @change="setTime('arrivalTime', 0, ($event.target as HTMLInputElement).value)" /><input aria-label="Latest arrival time" type="time" @change="setTime('arrivalTime', 1, ($event.target as HTMLInputElement).value)" /></div></fieldset>
    <label>Maximum bookings<input type="number" min="1" :max="filters?.maxBookingCount ?? undefined" :value="query.maxBookingCount" @change="set('maxBookingCount', Number(($event.target as HTMLInputElement).value) || undefined)" /></label>
    <label class="switch-filter"><input type="checkbox" :checked="query.allowAirportSwitches !== false" @change="set('allowAirportSwitches', ($event.target as HTMLInputElement).checked)" /> Allow airport changes</label>
  </aside>
</template>

<style scoped>
.filters { display: grid; align-content: start; gap: 14px; padding: 18px; border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--surface-raised); }
h2 { margin: 0; font-size: 1.05rem; }
label { display: grid; gap: 5px; color: var(--muted); font-size: .9rem; }
fieldset { display: grid; gap: 7px; margin: 0; padding: 10px; border: 1px solid var(--border); border-radius: 8px; }
fieldset label, .switch-filter { display: flex; align-items: center; gap: 7px; }
select, input[type='number'], input[type='time'] { width: 100%; box-sizing: border-box; padding: 8px; border: 1px solid var(--border); border-radius: 7px; background: var(--surface); color: var(--ink-strong); }
.time-range { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; }
</style>
