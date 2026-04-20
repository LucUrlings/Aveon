<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import type { SearchResult } from '../../features/flight-search/types'
import OneWaySearchResultCard from './OneWaySearchResultCard.vue'
import ReturnSearchResultCard from './ReturnSearchResultCard.vue'
import SyntheticReturnSearchResultCard from './SyntheticReturnSearchResultCard.vue'
import { formatResultForShare, isActualReturnFare, isSyntheticReturnFare } from './SearchResultCard.shared'

const props = defineProps<{
  result: SearchResult
  expanded: boolean
  selectedOutboundLegId?: string | null
  selectedReturnLegId?: string | null
}>()

const emit = defineEmits<{
  toggleExpanded: [resultId: string]
  filterLeg: [payload: { legId: string; legIndex: number }]
}>()

const copyState = ref<'idle' | 'copied' | 'failed'>('idle')
let copyResetTimer: number | null = null

const cardComponent = computed(() => {
  if (isSyntheticReturnFare(props.result)) {
    return SyntheticReturnSearchResultCard
  }

  if (isActualReturnFare(props.result)) {
    return ReturnSearchResultCard
  }

  return OneWaySearchResultCard
})

const copyLabel = computed(() => {
  if (copyState.value === 'copied') {
    return 'Copied'
  }

  if (copyState.value === 'failed') {
    return 'Failed'
  }

  return 'Copy'
})

const setCopyState = (nextState: 'idle' | 'copied' | 'failed') => {
  copyState.value = nextState

  if (copyResetTimer !== null) {
    window.clearTimeout(copyResetTimer)
  }

  if (nextState !== 'idle') {
    copyResetTimer = window.setTimeout(() => {
      copyState.value = 'idle'
      copyResetTimer = null
    }, 1500)
  }
}

const copyFare = async () => {
  const message = formatResultForShare(props.result, 1)

  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(message)
      setCopyState('copied')
      return
    }

    const textArea = document.createElement('textarea')
    textArea.value = message
    textArea.setAttribute('readonly', 'true')
    textArea.style.position = 'absolute'
    textArea.style.left = '-9999px'
    document.body.appendChild(textArea)
    textArea.select()
    const didCopy = document.execCommand('copy')
    document.body.removeChild(textArea)
    setCopyState(didCopy ? 'copied' : 'failed')
  } catch {
    setCopyState('failed')
  }
}

onBeforeUnmount(() => {
  if (copyResetTimer !== null) {
    window.clearTimeout(copyResetTimer)
  }
})
</script>

<template>
  <component
    :is="cardComponent"
    :result="result"
    :expanded="expanded"
    :copy-label="copyLabel"
    :selected-outbound-leg-id="selectedOutboundLegId"
    :selected-return-leg-id="selectedReturnLegId"
    @toggle-expanded="emit('toggleExpanded', $event)"
    @copy-fare="copyFare"
    @filter-leg="emit('filterLeg', $event)"
  />
</template>

<style scoped src="./SearchResultCard.css"></style>
