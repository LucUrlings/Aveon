<script setup lang="ts">
import type { ItineraryResult } from './types'
defineProps<{ result: ItineraryResult }>()
const duration = (minutes: number) => `${Math.floor(minutes / 60)}h ${minutes % 60}m`
const time = (value: string) => new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
</script>

<template>
  <article class="result-card">
    <header><div><strong>{{ result.currency }} {{ result.totalPrice.toFixed(2) }}</strong><span>{{ duration(result.totalFlightDurationMinutes) }} · {{ result.bookingCount }} separate bookings</span></div><span v-if="result.airportSwitches">{{ result.airportSwitches }} airport change</span></header>
    <ol>
      <li v-for="leg in result.legs" :key="`${result.id}-${leg.id}`"><b>{{ leg.originAirport }} → {{ leg.destinationAirport }}</b><span>{{ time(leg.departureLocalTime) }} – {{ time(leg.arrivalLocalTime) }} · {{ leg.stops === 0 ? 'Direct' : `${leg.stops} stop${leg.stops === 1 ? '' : 's'}` }}</span></li>
    </ol>
    <p v-for="warning in result.warnings" :key="warning.code" class="warning">{{ warning.message }}</p>
    <div class="booking-links"><a v-for="(booking, index) in result.bookingOptions" :key="`${booking.url}-${index}`" :href="booking.url" target="_blank" rel="noopener noreferrer">Book flight {{ index + 1 }} · {{ booking.currency }} {{ booking.price.toFixed(2) }}</a></div>
  </article>
</template>

<style scoped>
.result-card { padding: 18px; border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--surface-raised); }
header, header div { display: flex; justify-content: space-between; gap: 8px; } header div { flex-direction: column; } header strong { font-size: 1.2rem; } header span, li span { color: var(--muted); }
ol { display: grid; gap: 12px; padding-left: 24px; } li b, li span { display: block; }
.warning { color: #9b5c00; }
.booking-links { display: flex; flex-wrap: wrap; gap: 8px; }.booking-links a { padding: 9px 12px; border-radius: 8px; background: var(--brand); color: white; text-decoration: none; }
@media (max-width: 500px) { header { flex-direction: column; } }
</style>
