<script setup lang="ts">
import { computed } from 'vue'
import type { SearchResult } from '../../features/flight-search/types'
import { rankReturnOptions, type ReturnRanking } from '../../features/flight-search/returnRanking'
import { formatDuration, formatPrice } from './SearchResultCard.shared'

const props = defineProps<{
  results: SearchResult[]
  selectedRanking: ReturnRanking
}>()

const emit = defineEmits<{
  select: [ranking: ReturnRanking]
}>()

const options: Array<{ value: ReturnRanking; label: string; description: string }> = [
  { value: 'best', label: 'Recommended', description: 'Best overall value' },
  { value: 'cheapest', label: 'Cheapest', description: 'Lowest total fare' },
  { value: 'fastest', label: 'Fastest', description: 'Shortest return' },
]

const leadingResults = computed(() => Object.fromEntries(
  options.map((option) => [option.value, rankReturnOptions(props.results, option.value)[0] ?? null]),
) as Record<ReturnRanking, SearchResult | null>)

const returnDuration = (result: SearchResult) => result.legs[result.legs.length - 1]?.durationMinutes ?? result.totalDurationMinutes
</script>

<template>
  <section class="return-ranking-selector" aria-labelledby="return-ranking-title">
    <div class="return-ranking-heading">
      <div>
        <span>Sort return options</span>
        <strong id="return-ranking-title">What matters most for this trip?</strong>
      </div>
    </div>

    <div class="return-ranking-options" role="radiogroup" aria-labelledby="return-ranking-title">
      <button
        v-for="option in options"
        :key="option.value"
        type="button"
        :class="{ active: selectedRanking === option.value }"
        role="radio"
        :aria-checked="selectedRanking === option.value"
        @click="emit('select', option.value)"
      >
        <span class="return-ranking-label">
          <strong>{{ option.label }}</strong>
          <small>{{ option.description }}</small>
        </span>
        <span v-if="leadingResults[option.value]" class="return-ranking-summary">
          <strong>{{ formatPrice(leadingResults[option.value]!.priceOptions[0].totalPrice.amount, leadingResults[option.value]!.priceOptions[0].totalPrice.currency) }}</strong>
          <small>{{ formatDuration(returnDuration(leadingResults[option.value]!)) }} return</small>
        </span>
        <span v-else class="return-ranking-summary pending">
          <strong>—</strong>
          <small>Finding fares</small>
        </span>
      </button>
    </div>
  </section>
</template>

<style src="./ReturnOptions.css"></style>
