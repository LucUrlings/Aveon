<script setup lang="ts">
import { computed } from 'vue'
import type { SearchResult } from '../../features/flight-search/types'
import {
  formatDateTime,
  formatDuration,
  formatPrice,
  getAirlineSummary,
  getPrimaryBookingLink,
} from './SearchResultCard.shared'

const props = defineProps<{
  result: SearchResult
}>()

const airlineSummary = computed(() => getAirlineSummary(props.result))
const primaryBookingLink = computed(() => getPrimaryBookingLink(props.result))

const emit = defineEmits<{
  clear: []
}>()
</script>

<template>
  <section class="selected-outbound" aria-label="Selected outbound flight">
    <div class="selected-outbound-heading">
      <div>
        <span class="selected-outbound-label">Selected outbound</span>
        <strong>{{ airlineSummary }}</strong>
      </div>
      <button type="button" @click="emit('clear')">Change outbound</button>
    </div>

    <div v-if="result.legs[0]" class="selected-outbound-details">
      <strong class="selected-outbound-route">
        {{ result.legs[0].originAirport }} → {{ result.legs[0].destinationAirport }}
      </strong>
      <span>{{ formatDateTime(result.legs[0].departureLocalTime) }}</span>
      <span>Arrives {{ formatDateTime(result.legs[0].arrivalLocalTime) }}</span>
      <span>{{ formatDuration(result.legs[0].durationMinutes) }}</span>
      <span>
        {{ result.legs[0].segments.length <= 1 ? 'Direct' : `${result.legs[0].segments.length - 1} stop${result.legs[0].segments.length === 2 ? '' : 's'}` }}
      </span>
    </div>

    <div v-if="result.priceOptions[0]" class="selected-outbound-fare">
      <div class="selected-outbound-price">
        <span>Outbound only · return not included</span>
        <strong>
          {{ formatPrice(result.priceOptions[0].totalPrice.amount, result.priceOptions[0].totalPrice.currency) }}
        </strong>
      </div>
      <a
        v-if="primaryBookingLink"
        :href="primaryBookingLink.url"
        target="_blank"
        rel="noreferrer"
      >
        View outbound fare
      </a>
    </div>
  </section>
</template>

<style src="./ReturnOptions.css"></style>
