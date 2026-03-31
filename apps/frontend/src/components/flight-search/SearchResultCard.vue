<script setup lang="ts">
import { computed } from 'vue'
import type { SearchResult } from '../../features/flight-search/types'
import OneWaySearchResultCard from './OneWaySearchResultCard.vue'
import ReturnSearchResultCard from './ReturnSearchResultCard.vue'
import SyntheticReturnSearchResultCard from './SyntheticReturnSearchResultCard.vue'
import { isActualReturnFare, isSyntheticReturnFare } from './SearchResultCard.shared'

const props = defineProps<{
  result: SearchResult
  expanded: boolean
}>()

const emit = defineEmits<{
  toggleExpanded: [resultId: string]
}>()

const cardComponent = computed(() => {
  if (isSyntheticReturnFare(props.result)) {
    return SyntheticReturnSearchResultCard
  }

  if (isActualReturnFare(props.result)) {
    return ReturnSearchResultCard
  }

  return OneWaySearchResultCard
})
</script>

<template>
  <component
    :is="cardComponent"
    :result="result"
    :expanded="expanded"
    @toggle-expanded="emit('toggleExpanded', $event)"
  />
</template>
