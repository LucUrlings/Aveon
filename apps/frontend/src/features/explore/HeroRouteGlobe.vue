<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { getHeroRoutes } from './api'
import FlatRouteMap from './FlatRouteMap.vue'
import { localDateWithOffset } from './localDate'
import type { ExploreAirport, ExploreRoutesResponse } from './types'

const routes = ref<ExploreRoutesResponse | null>(null)
const failed = ref(false)
const selectedDestination = ref<ExploreAirport | null>(null)
const controller = new AbortController()
const exploreTarget = computed(() => {
  if (!routes.value) return '/explore'
  const path = selectedDestination.value
    ? `${routes.value.origin.code},${selectedDestination.value.code}`
    : routes.value.origin.code
  return `/explore?path=${encodeURIComponent(path)}&date=${localDateWithOffset(1)}`
})
const selectDestination = (airport: ExploreAirport) => { selectedDestination.value = airport }

onMounted(async () => {
  try { routes.value = await getHeroRoutes(controller.signal) }
  catch (reason) { if (!(reason instanceof Error && reason.name === 'AbortError')) failed.value = true }
})
onBeforeUnmount(() => controller.abort())
</script>

<template>
  <div class="hero-globe">
    <FlatRouteMap
      :routes="routes"
      :selected-destination="selectedDestination"
      @select="selectDestination"
    />
    <div class="hero-globe-footer">
      <div v-if="!routes" class="hero-globe-status" :class="{ failed }" role="status"><strong>{{ failed ? 'Route preview unavailable' : 'Loading routes…' }}</strong><small>{{ failed ? 'Open Explore to choose an airport.' : 'The route map is ready while the random hub catches up.' }}</small></div>
      <div v-else class="hero-globe-caption">
        <span>{{ selectedDestination ? 'Selected route' : 'Live preview' }}</span>
        <strong>{{ routes.origin.name }}<template v-if="selectedDestination"> → {{ selectedDestination.name }}</template></strong>
        <small v-if="selectedDestination">{{ routes.origin.city }} to {{ selectedDestination.city }} ({{ routes.origin.code }} → {{ selectedDestination.code }})</small>
        <small v-else>{{ routes.isComplete ? `${routes.destinations.length} current direct destinations` : `${routes.destinations.length}+ routes in this quick preview` }} · Click a city to choose</small>
      </div>
      <RouterLink class="hero-globe-link" :to="exploreTarget">{{ selectedDestination ? `Explore ${routes?.origin.city} to ${selectedDestination.city}` : 'Explore routes' }} <span aria-hidden="true">→</span></RouterLink>
    </div>
  </div>
</template>

<style scoped>
.hero-globe { display: grid; gap: 12px; min-height: 540px; }.hero-globe :deep(.flat-route-map) { min-height: 540px; }.hero-globe-footer { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 0 4px; }.hero-globe-status, .hero-globe-caption { display: grid; min-width: 0; gap: 2px; color: var(--ink-strong); }.hero-globe-status small, .hero-globe-caption small { overflow: hidden; color: var(--muted); text-overflow: ellipsis; white-space: nowrap; }.hero-globe-status.failed { color: #9a6000; }.hero-globe-caption span { color: var(--brand); font-size: .68rem; font-weight: 800; letter-spacing: .08em; text-transform: uppercase; }.hero-globe-caption strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }.hero-globe-link { flex: 0 0 auto; border-radius: 10px; padding: 10px 13px; background: var(--ink-strong); color: white; font-size: .82rem; font-weight: 760; text-decoration: none; }
@media (max-width: 680px) { .hero-globe, .hero-globe :deep(.flat-route-map) { min-height: 410px; }.hero-globe-footer { align-items: stretch; flex-direction: column; }.hero-globe-link { text-align: center; } }
</style>
