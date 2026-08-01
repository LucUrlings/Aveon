<script setup lang="ts">
import { ref } from 'vue'
import AirportGroupPicker from '../components/flight-search/AirportGroupPicker.vue'
import { useAirportPicker } from '../features/flight-search/useAirportPicker'
import OrderedRouteSearch from '../features/itinerary-search/OrderedRouteSearch.vue'

const activeTab = ref<'optimize' | 'ordered'>('optimize')
const optimizedStart = useAirportPicker([])
const optimizedDestination = useAirportPicker([])
const optimizedStartInput = optimizedStart.input
const optimizedStartAirports = optimizedStart.airports
const optimizedStartSuggestions = optimizedStart.suggestions
const optimizedDestinationInput = optimizedDestination.input
const optimizedDestinationAirports = optimizedDestination.airports
const optimizedDestinationSuggestions = optimizedDestination.suggestions
</script>

<template>
  <main id="main-content" class="advanced-page" tabindex="-1">
    <header>
      <p class="eyebrow">Multi-destination</p>
      <h1>Build a complete journey</h1>
      <p>Define airport groups now; route pricing and optimization arrive in the next milestones.</p>
    </header>

    <div class="advanced-tabs" role="tablist" aria-label="Multi-destination search mode">
      <button type="button" role="tab" :aria-selected="activeTab === 'optimize'" @click="activeTab = 'optimize'">Optimize my trip</button>
      <button type="button" role="tab" :aria-selected="activeTab === 'ordered'" @click="activeTab = 'ordered'">Build my route</button>
    </div>

    <section v-if="activeTab === 'optimize'" class="advanced-card" aria-label="Optimize my trip form">
      <AirportGroupPicker v-model:input="optimizedStartInput" v-model:airports="optimizedStartAirports" label="Starting airport group" input-aria-label="Add a starting airport or city" suggestions-aria-label="Starting airport suggestions" suggestion-id-prefix="optimized-start" :suggestions="optimizedStartSuggestions" @add-airport="optimizedStart.addAirport" @remove-airport="optimizedStart.removeAirport" @confirm-input="optimizedStart.confirmInput" />
      <AirportGroupPicker v-model:input="optimizedDestinationInput" v-model:airports="optimizedDestinationAirports" label="Destination airport group" input-aria-label="Add a destination airport or city" suggestions-aria-label="Destination airport suggestions" suggestion-id-prefix="optimized-destination" :suggestions="optimizedDestinationSuggestions" @add-airport="optimizedDestination.addAirport" @remove-airport="optimizedDestination.removeAirport" @confirm-input="optimizedDestination.confirmInput" />
    </section>

    <section v-else class="advanced-card advanced-card--ordered"><OrderedRouteSearch /></section>
  </main>
</template>

<style scoped>
.advanced-page { width: min(1000px, 100%); margin: 0 auto; padding: 48px 24px 72px; }
.advanced-page header { max-width: 720px; }
.advanced-page h1 { margin: 0; color: var(--ink-strong); }
.advanced-page header p:last-child { color: var(--muted); }
.advanced-tabs { display: flex; gap: 8px; margin: 28px 0 14px; }
.advanced-tabs button { border: 1px solid var(--border); border-radius: 999px; padding: 9px 14px; background: var(--surface); cursor: pointer; }
.advanced-tabs button[aria-selected='true'] { border-color: var(--brand); background: var(--brand-soft); color: var(--brand-strong); }
.advanced-card { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 18px; padding: 22px; border: 1px solid var(--border); border-radius: var(--radius-lg); background: var(--surface-raised); box-shadow: var(--shadow-md); }
.advanced-card--ordered { display: block; }
@media (max-width: 680px) { .advanced-page { padding: 32px 16px 56px; } .advanced-card { grid-template-columns: 1fr; } }
</style>
