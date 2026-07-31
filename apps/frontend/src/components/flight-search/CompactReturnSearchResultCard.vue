<script setup lang="ts">
import { computed } from 'vue'
import type { SearchResult } from '../../features/flight-search/types'
import {
  formatDateTime,
  formatDuration,
  formatPrice,
  formatProviderName,
  isSyntheticReturnFare,
} from './SearchResultCard.shared'

const props = defineProps<{
  result: SearchResult
  expanded: boolean
  copyLabel: string
  selectedOutboundLegId?: string | null
  selectedReturnLegId?: string | null
  showOutboundSelection?: boolean
}>()

const emit = defineEmits<{
  toggleExpanded: [resultId: string]
  copyFare: []
  filterLeg: [payload: { legId: string; legIndex: number }]
}>()

const returnLeg = computed(() => props.result.legs[props.result.legs.length - 1])
const returnAirlines = computed(() => [...new Set(
  returnLeg.value?.segments.map((segment) => segment.marketingCarrierName).filter(Boolean) ?? [],
)].join(', ') || 'Unknown airline')
const stopLabel = computed(() => {
  const stops = Math.max((returnLeg.value?.segments.length ?? 1) - 1, 0)
  return stops === 0 ? 'Direct' : `${stops} stop${stops === 1 ? '' : 's'}`
})
const primaryOption = computed(() => props.result.priceOptions[0])
const primaryLinks = computed(() => {
  const links = primaryOption.value?.bookingLinks ?? []
  if (!isSyntheticReturnFare(props.result)) {
    return links
  }

  const returnLinks = links.filter((link) => link.label.toLowerCase().includes('return'))
  return returnLinks.length > 0 ? returnLinks : links.slice(-1)
})
</script>

<template>
  <article v-if="returnLeg && primaryOption" class="compact-return-card">
    <div class="compact-return-flight">
      <div class="compact-return-title">
        <strong>{{ returnAirlines }}</strong>
        <span :class="['compact-booking-badge', { synthetic: isSyntheticReturnFare(result) }]">
          {{ isSyntheticReturnFare(result) ? 'Separate return ticket' : 'Round-trip fare' }}
        </span>
      </div>
      <div class="compact-return-timing">
        <strong>{{ formatDateTime(returnLeg.departureLocalTime) }}</strong>
        <span>{{ returnLeg.originAirport }} → {{ returnLeg.destinationAirport }}</span>
        <span>Arrives {{ formatDateTime(returnLeg.arrivalLocalTime) }}</span>
      </div>
      <div class="compact-return-meta">
        <span>{{ formatDuration(returnLeg.durationMinutes) }}</span>
        <span>{{ stopLabel }}</span>
        <span>{{ formatProviderName(primaryOption.provider) }}</span>
      </div>
    </div>

    <div class="compact-return-price">
      <span>Total trip</span>
      <strong>{{ formatPrice(primaryOption.totalPrice.amount, primaryOption.totalPrice.currency) }}</strong>
      <div class="compact-return-links">
        <a
          v-for="link in primaryLinks"
          :key="`${primaryOption.id}-${link.url}`"
          :href="link.url"
          target="_blank"
          rel="noreferrer"
        >
          {{ isSyntheticReturnFare(result) ? 'Book return' : (link.label || 'View fare') }}
          <small v-if="link.price">{{ formatPrice(link.price.amount, link.price.currency) }}</small>
        </a>
      </div>
    </div>

    <div class="compact-return-actions">
      <button
        type="button"
        :class="{ active: selectedReturnLegId === returnLeg.id }"
        @click="emit('filterLeg', { legId: returnLeg.id, legIndex: 1 })"
      >
        {{ selectedReturnLegId === returnLeg.id ? 'Return selected' : 'Select return' }}
      </button>
      <button type="button" :title="copyLabel" @click="emit('copyFare')">{{ copyLabel }}</button>
      <button
        v-if="result.priceOptions.length > 1"
        type="button"
        @click="emit('toggleExpanded', result.id)"
      >
        {{ expanded ? 'Hide sellers' : `${result.priceOptions.length - 1} more seller${result.priceOptions.length === 2 ? '' : 's'}` }}
      </button>
    </div>

    <Transition name="fare-expand">
      <ul v-if="result.priceOptions.length > 1 && expanded" class="compact-other-fares">
        <li v-for="option in result.priceOptions.slice(1)" :key="option.id">
          <span>{{ formatProviderName(option.provider) }}</span>
          <strong>{{ formatPrice(option.totalPrice.amount, option.totalPrice.currency) }}</strong>
          <a
            v-if="option.bookingLinks[option.bookingLinks.length - 1]"
            :href="option.bookingLinks[option.bookingLinks.length - 1]?.url"
            target="_blank"
            rel="noreferrer"
          >View fare</a>
        </li>
      </ul>
    </Transition>
  </article>
</template>

<style src="./ReturnOptions.css"></style>
