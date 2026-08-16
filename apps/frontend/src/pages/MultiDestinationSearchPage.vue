<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import OptimizedTripSearch from '../features/itinerary-search/OptimizedTripSearch.vue'
import OrderedRouteSearch from '../features/itinerary-search/OrderedRouteSearch.vue'

const route = useRoute()
const activeTab = ref<'optimize' | 'ordered'>(route.query.mode === 'optimize' ? 'optimize' : 'ordered')
const prefillRoute = computed(() => route.query.prefill === 'true' && typeof route.query.route === 'string'
  ? route.query.route.split(',').map(code => code.trim().toUpperCase()).filter(code => /^[A-Z]{3,4}$/.test(code))
  : [])
const prefillDepartureDate = computed(() => route.query.source === 'explore' && typeof route.query.departureDate === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(route.query.departureDate)
  ? route.query.departureDate
  : '')
const exploreHandoff = computed(() => route.query.source === 'explore' && prefillRoute.value.length > 1)
const modes = ['ordered', 'optimize'] as const
const selectMode = (mode: typeof modes[number]) => { activeTab.value = mode }
const moveMode = (current: typeof modes[number], direction: number) => {
  const index = modes.indexOf(current)
  const next = modes[(index + direction + modes.length) % modes.length]
  selectMode(next)
  window.setTimeout(() => document.getElementById(`multi-destination-tab-${next}`)?.focus())
}
watch(() => route.query.mode, mode => { activeTab.value = mode === 'optimize' ? 'optimize' : 'ordered' })
</script>

<template>
  <main id="main-content" class="advanced-page" tabindex="-1">
    <header>
      <p class="eyebrow">Multi-destination</p>
      <h1>Build a complete journey</h1>
      <p>Choose whether to keep a route in the exact order you enter it, or give Aveon an unordered set of places and let it compare possible journeys.</p>
      <p class="scope-note">Current results use separate one-way bookings. Bounded optimization shows the best complete trips found within its allowance and cannot guarantee the global cheapest route.</p>
    </header>

    <div class="advanced-tabs" role="tablist" aria-label="Multi-destination search mode">
      <button id="multi-destination-tab-ordered" type="button" role="tab" aria-controls="multi-destination-panel-ordered" :aria-selected="activeTab === 'ordered'" :tabindex="activeTab === 'ordered' ? 0 : -1" @click="selectMode('ordered')" @keydown.left.prevent="moveMode('ordered', -1)" @keydown.right.prevent="moveMode('ordered', 1)">Build my route</button>
      <button id="multi-destination-tab-optimize" type="button" role="tab" aria-controls="multi-destination-panel-optimize" :aria-selected="activeTab === 'optimize'" :tabindex="activeTab === 'optimize' ? 0 : -1" @click="selectMode('optimize')" @keydown.left.prevent="moveMode('optimize', -1)" @keydown.right.prevent="moveMode('optimize', 1)">Optimize my trip</button>
    </div>

    <section v-if="activeTab === 'optimize'" id="multi-destination-panel-optimize" role="tabpanel" aria-labelledby="multi-destination-tab-optimize" class="advanced-card advanced-card--full" tabindex="0"><OptimizedTripSearch /></section>

    <section v-else id="multi-destination-panel-ordered" role="tabpanel" aria-labelledby="multi-destination-tab-ordered" class="advanced-card advanced-card--ordered" tabindex="0"><OrderedRouteSearch :prefill-route="prefillRoute" :prefill-departure-date="prefillDepartureDate" :explore-handoff="exploreHandoff" /></section>
  </main>
</template>

<style scoped>
.advanced-page { width: min(var(--content-max), 100%); box-sizing: border-box; margin: 0 auto; padding: 48px 24px 72px; }
.advanced-page header { max-width: 720px; }
.advanced-page h1 { margin: 0; color: var(--ink-strong); }
.advanced-page header p:last-child { color: var(--muted); }
.scope-note { padding: 10px 12px; border-left: 3px solid var(--brand); background: var(--brand-soft); color: var(--ink-strong) !important; }
.advanced-tabs { display: flex; gap: 8px; margin: 28px 0 14px; }
.advanced-tabs button { border: 1px solid var(--border); border-radius: 999px; padding: 9px 14px; background: var(--surface); cursor: pointer; }
.advanced-tabs button[aria-selected='true'] { border-color: var(--brand); background: var(--brand-soft); color: var(--brand-strong); }
.advanced-card { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 18px; padding: 22px; border: 1px solid var(--border); border-radius: var(--radius-lg); background: var(--surface-raised); box-shadow: var(--shadow-md); }
.advanced-card--ordered { display: block; }
.advanced-card--full { display: block; }
@media (max-width: 680px) { .advanced-page { padding: 32px 16px 56px; } .advanced-card { grid-template-columns: 1fr; } }
</style>
