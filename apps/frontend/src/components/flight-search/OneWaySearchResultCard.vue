<script setup lang="ts">
import type { SearchResult } from '../../features/flight-search/types'
import {
  formatDateTime,
  formatDuration,
  formatProviderName,
  getAirlineSummary,
  getPrimaryBookingLink,
  isDirectFlight,
} from './SearchResultCard.shared'

defineProps<{
  result: SearchResult
  expanded: boolean
  copyLabel: string
}>()

const emit = defineEmits<{
  toggleExpanded: [resultId: string]
  copyFare: []
}>()
</script>

<template>
  <article class="result-card">
    <div class="details-header">
      <div class="details-main">
        <p class="provider">{{ getAirlineSummary(result) }}</p>
        <p class="route">
          {{ result.legs[0]?.originAirport }} to
          {{ result.legs[result.legs.length - 1]?.destinationAirport }}
        </p>
      </div>
      <div v-if="isDirectFlight(result)" class="details-timing">
        <span>
          {{ formatDateTime(result.legs[0]?.departureLocalTime ?? '') }} to
          {{ formatDateTime(result.legs[result.legs.length - 1]?.arrivalLocalTime ?? '') }}
        </span>
        <strong>{{ formatDuration(result.totalDurationMinutes) }}</strong>
      </div>
      <button class="copy-fare-button" type="button" :title="copyLabel" @click="emit('copyFare')">
        {{ copyLabel }}
      </button>
    </div>

    <div v-if="!isDirectFlight(result)">
      <div
        v-for="(leg, legIndex) in result.legs"
        :key="`${result.id}-${legIndex}`"
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
          <span class="fare-provider-label">{{ formatProviderName(result.priceOptions[0].provider) }}</span>
          <span>{{ formatDuration(result.totalDurationMinutes) }}</span>
        </div>
        <div class="price-block">
          <strong>
            {{ result.priceOptions[0].totalPrice.currency }}
            {{ result.priceOptions[0].totalPrice.amount.toFixed(2) }}
          </strong>
          <a
            v-if="getPrimaryBookingLink(result)"
            class="primary-fare-link"
            :href="getPrimaryBookingLink(result)?.url"
            target="_blank"
            rel="noreferrer"
          >
            {{ getPrimaryBookingLink(result)?.label || 'View fare' }}
          </a>
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
                {{ link.label || 'View fare' }}
              </a>
            </div>
          </li>
        </ul>
      </div>
    </Transition>
  </article>
</template>

<style scoped src="./SearchResultCard.css"></style>
