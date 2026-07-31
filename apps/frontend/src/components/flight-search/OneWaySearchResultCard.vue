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
  showOutboundSelection?: boolean
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
        <p class="provider">{{ airlineSummary }}</p>
        <p class="route">
          {{ result.legs[0]?.originAirport }} to
          {{ result.legs[result.legs.length - 1]?.destinationAirport }}
        </p>
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

    <div>
      <div
        v-for="(leg, legIndex) in result.legs"
        :key="`${result.id}-${legIndex}`"
        class="leg-block"
      >
        <div class="leg-summary">
          <p class="leg-route">{{ leg.originAirport }} → {{ leg.destinationAirport }}</p>
          <span class="leg-actions">
            <button
              v-if="showOutboundSelection"
              class="leg-filter-button"
              :class="{ active: selectedOutboundLegId === leg.id }"
              type="button"
              @click="emit('filterLeg', { legId: leg.id, legIndex: 0 })"
            >
              {{ selectedOutboundLegId === leg.id ? 'Selected outbound' : 'Choose outbound' }}
            </button>
          </span>
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
        @click="emit('toggleExpanded', result.id)"
      >
        {{ expanded ? 'Hide seller options' : `Show ${result.priceOptions.length - 1} ${result.priceOptions.length - 1 === 1 ? 'seller' : 'sellers'} for same flights` }}
      </button>
    </div>

    <Transition name="fare-expand">
      <div v-if="result.priceOptions.length > 1 && expanded" class="other-fares">
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
