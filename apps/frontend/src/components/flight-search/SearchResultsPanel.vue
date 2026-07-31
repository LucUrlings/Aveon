<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import type { ReturnRanking } from '../../features/flight-search/returnRanking'
import type { SearchResponse, SearchResult } from '../../features/flight-search/types'
import SearchResultCard from './SearchResultCard.vue'
import SelectedOutboundSummary from './SelectedOutboundSummary.vue'
import ReturnRankingSelector from './ReturnRankingSelector.vue'

const props = defineProps<{
  tripType: 'oneWay' | 'return'
  response: SearchResponse
  results: SearchResult[]
  isPolling: boolean
  isLoadingMore: boolean
  selectedOutboundLegId: string | null
  selectedReturnLegId: string | null
  selectedOutboundSummaryResult: SearchResult | null
  selectedRanking: ReturnRanking
  rankingLabel: string
  expandedResultIds: string[]
  currentPage: number
  hasMoreResults: boolean
  paginationSummary: string
  loadedStopCounts: { direct: number; oneStop: number; twoPlusStop: number }
}>()

const emit = defineEmits<{
  clearLegFilters: []
  selectRanking: [ranking: ReturnRanking]
  toggleExpanded: [resultId: string]
  filterLeg: [payload: { legId: string; legIndex: number }]
  loadMore: []
}>()

const loadMoreSentinel = ref<HTMLElement | null>(null)
let loadMoreObserver: IntersectionObserver | null = null

const isExpanded = (resultId: string) => props.expandedResultIds.includes(resultId)

const setupLoadMoreObserver = () => {
  loadMoreObserver?.disconnect()
  loadMoreObserver = null

  if (!loadMoreSentinel.value || typeof IntersectionObserver === 'undefined') {
    return
  }

  loadMoreObserver = new IntersectionObserver(
    (entries) => {
      if (entries.some((entry) => entry.isIntersecting) && props.hasMoreResults && !props.isPolling && !props.isLoadingMore) {
        emit('loadMore')
      }
    },
    { root: null, rootMargin: '0px 0px 320px 0px', threshold: 0 },
  )

  loadMoreObserver.observe(loadMoreSentinel.value)
}

// Re-arm the observer whenever a condition that may previously have suppressed
// loading changes. IntersectionObserver does not emit again merely because
// polling/loading finished while the sentinel remained visible.
watch(
  [
    loadMoreSentinel,
    () => props.hasMoreResults,
    () => props.isPolling,
    () => props.isLoadingMore,
    () => props.currentPage,
  ],
  setupLoadMoreObserver,
)

onBeforeUnmount(() => {
  loadMoreObserver?.disconnect()
})
</script>

<template>
  <section class="results-shell" aria-labelledby="search-results-title">
    <div class="results-header">
      <div>
        <p class="eyebrow">Results</p>
        <h2 id="search-results-title" aria-live="polite" aria-atomic="true">
          <template v-if="tripType === 'return' && !selectedOutboundLegId">
            {{ response.pagination.totalResults }} outbound flights to choose from
          </template>
          <template v-else-if="tripType === 'return'">
            {{ response.pagination.totalResults }} return options for your outbound
          </template>
          <template v-else>{{ response.pagination.totalResults }} flights after filters</template>
        </h2>
        <div v-if="selectedOutboundLegId || selectedReturnLegId" class="results-active-filters">
          <span class="active-filter-chip">
            {{ selectedOutboundLegId ? 'Outbound selected' : 'Return selected' }}
          </span>
          <button class="clear-active-filter" type="button" @click="emit('clearLegFilters')">Clear</button>
        </div>
      </div>
      <div class="results-stats">
        <span v-if="selectedOutboundLegId">Sorted: {{ rankingLabel }}</span>
        <span :title="`Loaded flights: ${results.length}\nDirect: ${loadedStopCounts.direct}\n1 stop: ${loadedStopCounts.oneStop}\n2+ stops: ${loadedStopCounts.twoPlusStop}`">
          {{ results.length }} loaded flights
          <template v-if="response.pagination.totalResults > 0">(out of {{ response.pagination.totalResults }})</template>
        </span>
        <span>{{ response.metadata.providerResultCount }} provider fares</span>
        <span>{{ response.metadata.searchCombinationCount }} search combinations</span>
      </div>
    </div>

    <SelectedOutboundSummary
      v-if="selectedOutboundSummaryResult"
      :result="selectedOutboundSummaryResult"
      @clear="emit('clearLegFilters')"
    />

    <ReturnRankingSelector
      v-if="selectedOutboundLegId"
      :results="results"
      :selected-ranking="selectedRanking"
      @select="emit('selectRanking', $event)"
    />

    <TransitionGroup name="result-list" tag="div" class="results-list">
      <SearchResultCard
        v-for="result in results"
        :key="result.id"
        :result="result"
        :expanded="isExpanded(result.id)"
        :selected-outbound-leg-id="selectedOutboundLegId"
        :selected-return-leg-id="selectedReturnLegId"
        :allow-outbound-selection="tripType === 'return'"
        :compact-return="Boolean(selectedOutboundLegId)"
        @toggle-expanded="emit('toggleExpanded', $event)"
        @filter-leg="emit('filterLeg', $event)"
      />
    </TransitionGroup>

    <div v-if="selectedOutboundLegId && results.length === 0" class="return-options-status" role="status" aria-live="polite">
      <strong>{{ isPolling ? 'Finding return options…' : 'No compatible return options found' }}</strong>
      <span v-if="isPolling">Recommendations will appear here as providers respond.</span>
      <span v-else>Try another outbound flight or broaden the search filters.</span>
    </div>

    <div v-if="response.pagination.totalPages > 1" class="pagination-bar">
      <span class="pagination-summary">{{ paginationSummary }}</span>
      <span class="pagination-page">Page {{ currentPage }} of {{ response.pagination.totalPages }}</span>
    </div>

    <div
      v-if="response.pagination.totalPages > 1 && hasMoreResults"
      ref="loadMoreSentinel"
      class="load-more-sentinel"
      aria-hidden="true"
    />
    <div v-if="isLoadingMore" class="load-more-status" role="status">Loading more fares…</div>
    <button
      v-else-if="hasMoreResults"
      class="load-more-button"
      type="button"
      @click="emit('loadMore')"
    >
      Load more flights
    </button>
  </section>
</template>

<style scoped>
.results-shell { border: 1px solid var(--border); border-radius: var(--radius-lg); background: var(--surface-raised); box-shadow: var(--shadow-md); backdrop-filter: blur(20px); padding: 20px; }
.results-header { display: flex; justify-content: space-between; align-items: end; gap: 16px; margin-bottom: 18px; }
.results-header > div, .results-stats, .results-stats span { min-width: 0; }
.eyebrow { margin: 0 0 8px; font-size: 0.7rem; font-weight: 800; letter-spacing: 0.16em; text-transform: uppercase; color: var(--brand); }
h2 { margin: 0; color: var(--ink-strong); }
.results-stats { display: flex; flex-wrap: wrap; justify-content: end; gap: 7px; color: var(--muted); font-size: 0.78rem; }
.results-stats span { padding: 6px 9px; border-radius: 999px; border: 1px solid #e6eaf1; background: var(--surface-subtle); }
.results-active-filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-top: 8px; }
.active-filter-chip { display: inline-flex; align-items: center; border-radius: 999px; padding: 5px 8px; background: var(--brand-soft); color: var(--brand-strong); font-size: 0.78rem; font-weight: 700; }
.clear-active-filter { border: none; background: transparent; color: var(--muted); font: inherit; font-size: 0.78rem; font-weight: 600; cursor: pointer; padding: 0; }
.clear-active-filter:hover { color: var(--ink-strong); }
.results-list { display: grid; gap: 12px; }
.return-options-status { display: grid; gap: 0.35rem; padding: 1.25rem; border: 1px dashed var(--border); border-radius: 0.9rem; color: var(--muted); text-align: center; }
.return-options-status strong { color: var(--ink-strong); }
.pagination-bar { display: flex; justify-content: space-between; align-items: center; gap: 12px; margin-top: 14px; padding-top: 12px; border-top: 1px solid var(--border); }
.pagination-summary { color: var(--muted); font-size: 0.82rem; }
.pagination-page { color: var(--ink); font-size: 0.82rem; font-weight: 600; }
.load-more-sentinel { height: 1px; }
.load-more-status { margin-top: 10px; color: var(--muted); font-size: 0.82rem; text-align: center; }
.load-more-button { display: block; margin: 12px auto 0; border: 1px solid var(--border-strong); border-radius: 999px; background: var(--surface-raised); color: var(--ink-strong); padding: 8px 16px; font: inherit; font-size: 0.82rem; font-weight: 700; cursor: pointer; }
.load-more-button:hover { border-color: var(--brand); color: var(--brand-strong); background: var(--brand-soft); }
.load-more-button:focus-visible { outline: 3px solid var(--focus-ring); outline-offset: 2px; }
.result-list-enter-active, .result-list-leave-active { transition: opacity 0.16s ease, transform 0.16s ease; }
.result-list-move { transition: transform 0.16s ease; }
.result-list-enter-from { opacity: 0; transform: translateY(6px) scale(0.995); }
.result-list-leave-to { opacity: 0; transform: translateY(-4px) scale(0.995); }
@media (max-width: 960px) { .results-header { align-items: start; flex-direction: column; } .results-stats { justify-content: start; } .pagination-bar { flex-direction: column; align-items: start; } }
@media (max-width: 640px) { .results-shell { padding-left: 10px; padding-right: 10px; border-radius: 10px; } .results-header { gap: 8px; } .results-stats { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 5px; font-size: 0.78rem; width: 100%; } .results-stats span { padding: 4px 7px; border-radius: 8px; text-align: center; overflow-wrap: anywhere; } .results-list { gap: 10px; } .load-more-status { font-size: 0.78rem; } }
@media (max-width: 420px) { .results-stats { grid-template-columns: 1fr; } }
</style>
