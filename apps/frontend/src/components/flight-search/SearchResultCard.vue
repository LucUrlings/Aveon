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
  const match = value.match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/)
  if (!match) {
    return value
  }

  const [, year, month, day, hours, minutes] = match
  const date = new Date(Number.parseInt(year, 10), Number.parseInt(month, 10) - 1, Number.parseInt(day, 10))
  const weekday = new Intl.DateTimeFormat('en-IE', {
    weekday: 'short',
  }).format(date).slice(0, 2)
  const monthLabel = new Intl.DateTimeFormat('en-IE', {
    month: 'short',
  }).format(date)

  return `${weekday} ${day} ${monthLabel} ${hours}:${minutes}`
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
          {{ formatDateTime(props.result.legs[0]?.departureLocalTime ?? '') }} to
          {{ formatDateTime(props.result.legs[props.result.legs.length - 1]?.arrivalLocalTime ?? '') }}
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
          <span class="leg-times">{{ formatDateTime(leg.departureLocalTime) }} to {{ formatDateTime(leg.arrivalLocalTime) }}</span>
          <strong>{{ formatDuration(leg.durationMinutes) }}</strong>
        </div>

        <ul class="segment-list">
          <li
            v-for="segment in leg.segments"
            :key="segment.flightNumber + segment.departureLocalTime"
            class="segment-item"
          >
            <span class="segment-airline">{{ segment.marketingCarrierName }} ({{ segment.marketingCarrierCode }}) {{ segment.flightNumber }}</span>
            <span class="segment-route">{{ segment.originAirport }} → {{ segment.destinationAirport }}</span>
            <span class="segment-times">{{ formatDateTime(segment.departureLocalTime) }} to {{ formatDateTime(segment.arrivalLocalTime) }}</span>
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

<style scoped src="./SearchResultCard.css"></style>
