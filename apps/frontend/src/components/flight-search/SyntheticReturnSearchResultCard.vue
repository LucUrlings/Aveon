<script setup lang="ts">
import type { SearchResult } from '../../features/flight-search/types'
import {
  formatDateTime,
  formatDuration,
  formatProviderName,
  getAirlineSummary,
} from './SearchResultCard.shared'

defineProps<{
  result: SearchResult
  expanded: boolean
}>()

const emit = defineEmits<{
  toggleExpanded: [resultId: string]
}>()
</script>

<template>
  <article class="result-card">
    <div class="details-header">
      <div class="details-main">
        <p class="trip-badge synthetic">Separate bookings</p>
        <p class="provider">{{ getAirlineSummary(result) }}</p>
        <p class="route">
          {{ result.legs[0]?.originAirport }} round trip to
          {{ result.legs[0]?.destinationAirport }}
        </p>
      </div>
      <div class="details-timing">
        <span>
          {{ formatDateTime(result.legs[0]?.departureLocalTime ?? '') }} outbound
        </span>
        <span>
          {{ formatDateTime(result.legs[result.legs.length - 1]?.departureLocalTime ?? '') }} return
        </span>
        <strong>{{ formatDuration(result.totalDurationMinutes) }}</strong>
      </div>
    </div>

    <div
      v-for="(leg, legIndex) in result.legs"
      :key="`${result.id}-${legIndex}`"
      class="leg-block"
    >
      <div class="leg-summary">
        <div class="return-leg-copy">
          <span class="return-leg-label">{{ legIndex === 0 ? 'Outbound' : 'Return' }}</span>
          <p class="leg-route">{{ leg.originAirport }} → {{ leg.destinationAirport }}</p>
        </div>
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

    <div class="fare-stack">
      <div class="fare-summary">
        <div class="fare-provider">
          <span class="fare-provider-label">{{ formatProviderName(result.priceOptions[0].provider) }}</span>
          <span>Two separate bookings</span>
        </div>
        <div class="price-block">
          <strong>
            {{ result.priceOptions[0].totalPrice.currency }}
            {{ result.priceOptions[0].totalPrice.amount.toFixed(2) }}
          </strong>
          <div class="primary-booking-links">
            <a
              v-for="link in result.priceOptions[0].bookingLinks"
              :key="`${result.priceOptions[0].id}-${link.url}`"
              class="primary-fare-link"
              :href="link.url"
              target="_blank"
              rel="noreferrer"
            >
              {{ link.label }}
            </a>
          </div>
        </div>
      </div>

      <button
        v-if="result.priceOptions.length > 1"
        class="expand-button attached-expand"
        type="button"
        @click="emit('toggleExpanded', result.id)"
      >
        {{ expanded ? 'Hide other fares' : `Show ${result.priceOptions.length - 1} more fares` }}
      </button>
    </div>

    <Transition name="fare-expand">
      <div v-if="result.priceOptions.length > 1 && expanded" class="other-fares">
        <p class="other-fares-title">Other fare options</p>
        <ul class="other-fares-list">
          <li
            v-for="option in result.priceOptions.slice(1)"
            :key="option.id"
            class="other-fare-item"
          >
            <div>
              <strong>{{ formatProviderName(option.provider) }}</strong>
              <span>{{ option.totalPrice.currency }} {{ option.totalPrice.amount.toFixed(2) }}</span>
            </div>
            <div class="other-fare-links">
              <a
                v-for="link in option.bookingLinks"
                :key="`${option.id}-${link.url}`"
                :href="link.url"
                target="_blank"
                rel="noreferrer"
              >
                {{ link.label }}
              </a>
            </div>
          </li>
        </ul>
      </div>
    </Transition>
  </article>
</template>

<style scoped src="./SearchResultCard.css"></style>
