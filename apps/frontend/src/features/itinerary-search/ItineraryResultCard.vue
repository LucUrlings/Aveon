<script setup lang="ts">
import type { ItineraryResult } from './types'
import { trackItineraryEvent } from './analytics'
defineProps<{ result: ItineraryResult }>()
const duration = (minutes: number) => `${Math.floor(minutes / 60)}h ${minutes % 60}m`
const time = (value: string) => new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
const selectResult = (event: Event, bookingType: string) => {
  if ((event.currentTarget as HTMLDetailsElement).open) trackItineraryEvent('result_selection', { booking_type: bookingType })
}
const selectBooking = (result: ItineraryResult, position: number) => trackItineraryEvent('booking_click', {
  booking_type: result.bookingType,
  booking_count: result.bookingCount,
  position,
})
</script>

<template>
  <article class="result-card">
    <header>
      <div><strong>{{ result.currency }} {{ result.totalPrice.toFixed(2) }}</strong><span>{{ duration(result.totalFlightDurationMinutes) }} in the air · {{ result.totalStops }} total stops</span></div>
      <div class="badges"><span class="badge">Separate tickets</span><span class="badge">{{ result.bookingCount }} transaction{{ result.bookingCount === 1 ? '' : 's' }}</span><span v-if="result.airportSwitches" class="badge badge--warning">{{ result.airportSwitches }} airport change{{ result.airportSwitches === 1 ? '' : 's' }}</span></div>
    </header>
    <ol class="timeline" aria-label="Complete itinerary timeline">
      <li v-for="(leg, index) in result.legs" :key="`${result.id}-${leg.id}-${index}`">
        <div class="timeline-marker" aria-hidden="true"></div>
        <div class="timeline-content">
          <b>Flight {{ index + 1 }} · {{ leg.originAirport }} → {{ leg.destinationAirport }}</b>
          <span>{{ time(leg.departureLocalTime) }} – {{ time(leg.arrivalLocalTime) }}</span>
          <span>{{ duration(leg.durationMinutes) }} · {{ leg.stops === 0 ? 'Direct' : `${leg.stops} stop${leg.stops === 1 ? '' : 's'}` }}<template v-if="result.bookingOptions[index]"> · {{ result.bookingOptions[index].currency }} {{ result.bookingOptions[index].price.toFixed(2) }}</template></span>
          <details v-if="leg.segments?.length" class="flight-details"><summary>Flight details</summary><ul><li v-for="(segment, segmentIndex) in leg.segments" :key="`${leg.id}-${segment.flightNumber}-${segmentIndex}`">{{ segment.marketingCarrierName || segment.marketingCarrierCode }} {{ segment.flightNumber }} · {{ segment.originAirport }} → {{ segment.destinationAirport }}</li></ul></details>
          <p v-if="result.stays[index]" class="stay">Stay {{ result.stays[index].nights }} night{{ result.stays[index].nights === 1 ? '' : 's' }} · {{ result.stays[index].arrivalDate }} to {{ result.stays[index].departureDate }}</p>
        </div>
      </li>
    </ol>
    <div v-if="result.warnings.length" class="warnings" role="status" aria-live="polite" aria-label="Important booking warnings"><strong>Before you book</strong><p v-for="warning in result.warnings" :key="warning.code" class="warning">{{ warning.message }}</p></div>
    <details class="score-explanation" @toggle="selectResult($event, result.bookingType)"><summary>Why this itinerary ranks here</summary><dl><div><dt>Ranking score</dt><dd>{{ result.rankingBreakdown.score.toFixed(2) }}</dd></div><div><dt>Total price</dt><dd>{{ result.currency }} {{ result.rankingBreakdown.totalPrice.toFixed(2) }}</dd></div><div><dt>Extra flight time</dt><dd>{{ duration(result.rankingBreakdown.additionalFlightMinutes) }}</dd></div><div><dt>Stops</dt><dd>{{ result.rankingBreakdown.totalStops }}</dd></div><div><dt>Additional bookings</dt><dd>{{ result.rankingBreakdown.additionalBookings }}</dd></div><div><dt>Airport changes</dt><dd>{{ result.rankingBreakdown.airportSwitches }}</dd></div></dl></details>
    <div class="booking-links"><a v-for="(booking, index) in result.bookingOptions" :key="`${booking.url}-${index}`" :href="booking.url" target="_blank" rel="noopener noreferrer" @click="selectBooking(result, index + 1)">Book flight {{ index + 1 }} · {{ booking.currency }} {{ booking.price.toFixed(2) }}</a></div>
  </article>
</template>

<style scoped>
.result-card { display: grid; gap: 16px; padding: 20px; border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--surface-raised); }
header, header > div { display: flex; justify-content: space-between; gap: 8px; } header > div:first-child { flex-direction: column; } header strong { font-size: 1.25rem; } header span, .timeline-content > span { color: var(--muted); }
.badges { align-items: flex-start; flex-wrap: wrap; justify-content: flex-end; }.badge { padding: 5px 8px; border-radius: 999px; background: var(--brand-soft); color: var(--ink-strong); font-size: .78rem; }.badge--warning { background: #fff4db; color: #7a4b00; }
.timeline { display: grid; gap: 0; margin: 0; padding: 0; list-style: none; }.timeline > li { position: relative; display: grid; grid-template-columns: 18px minmax(0, 1fr); gap: 10px; padding-bottom: 18px; }.timeline > li:not(:last-child)::before { content: ''; position: absolute; left: 6px; top: 13px; bottom: -1px; width: 2px; background: var(--border); }.timeline-marker { width: 14px; height: 14px; margin-top: 3px; border: 3px solid var(--brand); border-radius: 50%; background: var(--surface-raised); z-index: 1; }.timeline-content { min-width: 0; }.timeline-content b, .timeline-content > span { display: block; }.stay { margin: 10px 0 0; padding: 8px 10px; border-radius: 8px; background: var(--brand-soft); color: var(--ink-strong); font-size: .88rem; }
.flight-details { margin-top: 7px; }.flight-details summary, .score-explanation summary { cursor: pointer; color: var(--brand-strong); }.flight-details ul { margin: 7px 0 0; padding-left: 20px; color: var(--muted); }
.warnings { padding: 12px; border-left: 4px solid #d98b00; border-radius: 8px; background: #fff8e8; }.warnings strong { color: #6f4400; }.warning { margin: 5px 0 0; color: #7a4b00; }
.score-explanation dl { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 8px; }.score-explanation dl div { padding: 8px; border-radius: 8px; background: var(--surface); }.score-explanation dt { color: var(--muted); font-size: .78rem; }.score-explanation dd { margin: 3px 0 0; font-weight: 700; }
.booking-links { display: flex; flex-wrap: wrap; gap: 8px; }.booking-links a { padding: 9px 12px; border-radius: 8px; background: var(--brand); color: white; text-decoration: none; }
@media (max-width: 500px) { header { flex-direction: column; } }
@media (max-width: 560px) { .badges { justify-content: flex-start; }.score-explanation dl { grid-template-columns: 1fr 1fr; }.booking-links a { width: 100%; box-sizing: border-box; text-align: center; } }
</style>
