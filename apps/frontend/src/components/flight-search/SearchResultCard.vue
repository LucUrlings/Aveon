<script setup lang="ts">
import type { SearchResult } from '../../features/flight-search/types'

const props = defineProps<{
  result: SearchResult
  expanded: boolean
}>()

const emit = defineEmits<{
  toggleExpanded: [resultId: string]
}>()

const formatDateTime = (value: string) => {
  const date = new Date(value)
  const weekday = new Intl.DateTimeFormat('en-IE', {
    weekday: 'short',
    timeZone: 'UTC',
  }).format(date).slice(0, 2)

  const rest = new Intl.DateTimeFormat('en-IE', {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: 'UTC',
  }).format(date)

  return `${weekday} ${rest}`
}

const formatDuration = (totalMinutes: number) => {
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  return `${hours}h ${minutes}m`
}

const formatProviderName = (provider: string) => provider.replace(/^FlightApi:/, '').trim()

const getAirlineSummary = (result: SearchResult) => {
  const airlines = [...new Set(
    result.legs.flatMap((leg) =>
      leg.segments.map((segment) => segment.marketingCarrierName),
    ),
  )].filter(Boolean)

  return airlines.join(', ') || 'Unknown airline'
}

const isDirectFlight = (result: SearchResult) =>
  result.legs.every((leg) => leg.segments.length === 1)
</script>

<template>
  <article class="result-card">
    <div class="details-header">
      <div class="details-main">
        <p class="provider">{{ getAirlineSummary(props.result) }}</p>
        <p class="route">
          {{ props.result.legs[0]?.originAirport }} to
          {{ props.result.legs[props.result.legs.length - 1]?.destinationAirport }}
        </p>
      </div>
      <div v-if="isDirectFlight(props.result)" class="details-timing">
        <span>
          {{ formatDateTime(props.result.legs[0]?.departureUtc ?? '') }} to
          {{ formatDateTime(props.result.legs[props.result.legs.length - 1]?.arrivalUtc ?? '') }}
        </span>
        <strong>{{ formatDuration(props.result.totalDurationMinutes) }}</strong>
      </div>
    </div>

    <div v-if="!isDirectFlight(props.result)">
      <div
        v-for="(leg, legIndex) in props.result.legs"
        :key="`${props.result.id}-${legIndex}`"
        class="leg-block"
      >
        <div class="leg-summary">
          <p class="leg-route">{{ leg.originAirport }} → {{ leg.destinationAirport }}</p>
          <span class="leg-times">{{ formatDateTime(leg.departureUtc) }} to {{ formatDateTime(leg.arrivalUtc) }}</span>
          <strong>{{ formatDuration(leg.durationMinutes) }}</strong>
        </div>

        <ul class="segment-list">
          <li
            v-for="segment in leg.segments"
            :key="segment.flightNumber + segment.departureUtc"
            class="segment-item"
          >
            <span class="segment-airline">{{ segment.marketingCarrierName }} ({{ segment.marketingCarrierCode }}) {{ segment.flightNumber }}</span>
            <span class="segment-route">{{ segment.originAirport }} → {{ segment.destinationAirport }}</span>
            <span class="segment-times">{{ formatDateTime(segment.departureUtc) }} to {{ formatDateTime(segment.arrivalUtc) }}</span>
          </li>
        </ul>
      </div>
    </div>

    <div class="fare-stack">
      <div class="fare-summary">
        <div class="fare-provider">
          <span class="fare-provider-label">{{ formatProviderName(props.result.priceOptions[0].provider) }}</span>
          <span>{{ formatDuration(props.result.totalDurationMinutes) }}</span>
        </div>
        <div class="price-block">
          <strong>
            {{ props.result.priceOptions[0].totalPrice.currency }}
            {{ props.result.priceOptions[0].totalPrice.amount.toFixed(2) }}
          </strong>
          <a
            v-if="props.result.priceOptions[0].deepLink"
            class="primary-fare-link"
            :href="props.result.priceOptions[0].deepLink"
            target="_blank"
            rel="noreferrer"
          >
            View fare
          </a>
        </div>
      </div>

      <button
        v-if="props.result.priceOptions.length > 1"
        class="expand-button attached-expand"
        type="button"
        @click="emit('toggleExpanded', props.result.id)"
      >
        {{ props.expanded ? 'Hide other fares' : `Show ${props.result.priceOptions.length - 1} more fares` }}
      </button>
    </div>

    <Transition name="fare-expand">
      <div v-if="props.result.priceOptions.length > 1 && props.expanded" class="other-fares">
        <p class="other-fares-title">Other fare options</p>
        <ul class="other-fares-list">
          <li
            v-for="option in props.result.priceOptions.slice(1)"
            :key="option.id"
            class="other-fare-item"
          >
            <div>
              <strong>{{ formatProviderName(option.provider) }}</strong>
              <span>{{ option.totalPrice.currency }} {{ option.totalPrice.amount.toFixed(2) }}</span>
            </div>
            <a
              v-if="option.deepLink"
              :href="option.deepLink"
              target="_blank"
              rel="noreferrer"
            >
              View fare
            </a>
          </li>
        </ul>
      </div>
    </Transition>
  </article>
</template>

<style scoped>
.result-card {
  padding: 10px 12px;
  border: 1px solid rgba(29, 34, 40, 0.08);
  border-radius: 10px;
  background: #fff;
  box-shadow: 0 6px 16px rgba(41, 49, 61, 0.04);
}

.details-header {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: start;
  margin-bottom: 8px;
}

.details-main {
  display: grid;
  gap: 4px;
}

.details-timing {
  display: grid;
  gap: 4px;
  justify-items: end;
  color: #5b6570;
  text-align: right;
}

.provider,
.route {
  margin: 0;
}

.provider {
  font-size: 0.9rem;
  font-weight: 600;
}

.route {
  color: #5b6570;
  font-size: 0.84rem;
}

.leg-block + .leg-block {
  margin-top: 8px;
}

.leg-summary {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.35fr) auto;
  gap: 10px;
  align-items: center;
}

.leg-route,
.leg-times {
  margin: 0;
}

.leg-route {
  font-weight: 700;
  font-size: 0.88rem;
}

.leg-times {
  color: #5b6570;
  white-space: nowrap;
  font-size: 0.82rem;
}

.segment-list {
  list-style: none;
  margin: 6px 0 0;
  padding: 0;
  display: grid;
  gap: 6px;
}

.segment-item {
  display: grid;
  grid-template-columns: minmax(0, 1.35fr) minmax(0, 0.75fr) minmax(0, 1fr);
  gap: 10px;
  align-items: center;
  padding: 6px 8px;
  border-radius: 8px;
  background: #f5f7fa;
  color: #33404d;
  font-size: 0.82rem;
}

.segment-airline,
.segment-route,
.segment-times {
  min-width: 0;
}

.segment-route,
.segment-times {
  white-space: nowrap;
}

.segment-times {
  text-align: right;
  color: #5f6973;
}

.fare-stack {
  width: 100%;
  margin-top: 8px;
}

.fare-summary {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: center;
  padding: 9px 10px;
  border-radius: 8px 8px 0 0;
  background: linear-gradient(135deg, #fff3da 0%, #fdf7ea 100%);
}

.fare-provider {
  display: grid;
  gap: 4px;
  font-size: 0.82rem;
}

.fare-provider-label {
  font-weight: 700;
}

.price-block {
  display: grid;
  gap: 4px;
  justify-items: end;
}

.price-block strong {
  font-size: 1rem;
  font-weight: 700;
}

.primary-fare-link,
.other-fare-item a {
  color: #1f5fbf;
  font-weight: 600;
  text-decoration: none;
  font-size: 0.84rem;
}

.attached-expand {
  width: 100%;
  border: 1px solid rgba(31, 95, 191, 0.18);
  border-top: none;
  border-radius: 0 0 8px 8px;
  padding: 7px 10px;
  background: #f7fbff;
  color: #1f5fbf;
  font: inherit;
  font-weight: 600;
  text-align: center;
  cursor: pointer;
  font-size: 0.84rem;
}

.other-fares {
  margin-top: 8px;
  padding: 8px 10px;
  border-radius: 8px;
  background: #f7f8fb;
}

.other-fares-title {
  margin: 0 0 6px;
  font-weight: 600;
  font-size: 0.84rem;
}

.other-fares-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 6px;
}

.other-fare-item {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  align-items: center;
  padding: 6px 8px;
  border-radius: 8px;
  background: #fff;
  font-size: 0.82rem;
}

.other-fare-item div {
  display: grid;
  gap: 2px;
}

.fare-expand-enter-active,
.fare-expand-leave-active {
  transition:
    opacity 0.18s ease,
    transform 0.18s ease,
    max-height 0.18s ease,
    margin-top 0.18s ease;
  overflow: hidden;
}

.fare-expand-enter-from,
.fare-expand-leave-to {
  opacity: 0;
  transform: translateY(-4px);
  max-height: 0;
  margin-top: 0;
}

.fare-expand-enter-to,
.fare-expand-leave-from {
  opacity: 1;
  transform: translateY(0);
  max-height: 240px;
  margin-top: 8px;
}

@media (max-width: 720px) {
  .details-header,
  .leg-summary,
  .segment-item,
  .fare-summary,
  .other-fare-item {
    grid-template-columns: 1fr;
    display: grid;
  }

  .details-timing {
    justify-items: start;
    text-align: left;
  }

  .leg-times,
  .segment-route,
  .segment-times {
    white-space: normal;
  }

  .segment-times {
    text-align: left;
  }

  .price-block {
    justify-items: start;
  }
}
</style>
