<script setup lang="ts">
import { computed } from 'vue'
import type { SearchResult } from '../../features/flight-search/types'
import {
  formatDateTime,
  formatDuration,
  formatProviderName,
  getAirlineSummary,
  getFareDifferenceBadges,
  getFareIdentityChips,
  getPrimaryBookingLink,
} from './SearchResultCard.shared'

const props = defineProps<{
  result: SearchResult
  expanded: boolean
  copyLabel: string
  selectedOutboundLegId?: string | null
  selectedReturnLegId?: string | null
}>()

const airlineSummary = computed(() => getAirlineSummary(props.result))
const identityChips = computed(() => getFareIdentityChips(props.result))
const fareDifferenceBadges = computed(() => getFareDifferenceBadges(props.result))
const primaryBookingLink = computed(() => getPrimaryBookingLink(props.result))

const emit = defineEmits<{
  toggleExpanded: [resultId: string]
  copyFare: []
  filterLeg: [payload: { legId: string; legIndex: number }]
}>()
</script>

<template>
  <article class="result-card">
    <div class="details-header">
      <div class="details-main">
        <p class="trip-badge">Round trip</p>
        <p class="provider">{{ airlineSummary }}</p>
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
      <button class="copy-fare-button" type="button" :title="copyLabel" @click="emit('copyFare')">
        {{ copyLabel }}
      </button>
    </div>

    <div class="identity-chip-row" aria-label="Fare details">
      <span
        v-for="chip in identityChips"
        :key="chip"
        class="identity-chip"
      >
        {{ chip }}
      </span>
    </div>

    <div v-if="fareDifferenceBadges.length" class="difference-badge-row" aria-label="Fare notices">
      <span
        v-for="badge in fareDifferenceBadges"
        :key="`${badge.tone}-${badge.label}`"
        class="difference-badge"
        :class="`tone-${badge.tone}`"
      >
        {{ badge.label }}
      </span>
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
        <div class="leg-actions">
          <button
            class="leg-filter-button"
            :class="{ active: legIndex === 0 ? selectedOutboundLegId === leg.id : selectedReturnLegId === leg.id }"
            type="button"
            @click="emit('filterLeg', { legId: leg.id, legIndex })"
          >
            {{ legIndex === 0 ? (selectedOutboundLegId === leg.id ? 'Selected leg' : 'Show combos') : (selectedReturnLegId === leg.id ? 'Selected leg' : 'Show combos') }}
          </button>
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
          <span>Single round-trip booking</span>
        </div>
        <div class="price-block">
          <strong>
            {{ result.priceOptions[0].totalPrice.currency }}
            {{ result.priceOptions[0].totalPrice.amount.toFixed(2) }}
          </strong>
          <a
            v-if="primaryBookingLink"
            class="primary-fare-link"
            :href="primaryBookingLink.url"
            target="_blank"
            rel="noreferrer"
          >
            {{ primaryBookingLink.label || 'View fare' }}
          </a>
        </div>
      </div>

      <button
        v-if="result.priceOptions.length > 1"
        class="expand-button attached-expand"
        type="button"
        :aria-expanded="expanded"
        :aria-controls="`seller-options-${result.id}`"
        @click="emit('toggleExpanded', result.id)"
      >
        {{ expanded ? 'Hide seller options' : `Show ${result.priceOptions.length - 1} ${result.priceOptions.length - 1 === 1 ? 'seller' : 'sellers'} for same flights` }}
      </button>
    </div>

    <Transition name="fare-expand">
      <div v-if="result.priceOptions.length > 1 && expanded" :id="`seller-options-${result.id}`" class="other-fares">
        <p class="other-fares-title">Sellers for the same flights</p>
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
