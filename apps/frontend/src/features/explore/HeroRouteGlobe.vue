<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { getHeroRoutes } from './api'
import RouteGlobe from './RouteGlobe.vue'
import type { ExploreRoutesResponse } from './types'

const routes = ref<ExploreRoutesResponse | null>(null)
const failed = ref(false)
const controller = new AbortController()

onMounted(async () => {
  try { routes.value = await getHeroRoutes(controller.signal) }
  catch (reason) { if (!(reason instanceof Error && reason.name === 'AbortError')) failed.value = true }
})
onBeforeUnmount(() => controller.abort())
</script>

<template>
  <div class="hero-globe">
    <RouteGlobe :routes="routes" :interactive="false" :allow-zoom="false" />
    <div v-if="!routes" class="hero-globe-status" :class="{ failed }" role="status"><strong>{{ failed ? 'Route preview unavailable' : 'Loading routes…' }}</strong><small>{{ failed ? 'Open Explore to choose an airport.' : 'The map is ready while the random hub catches up.' }}</small></div>
    <div v-if="routes" class="hero-globe-caption"><span>Live preview</span><strong>{{ routes.origin.city }} · {{ routes.origin.code }}</strong><small>{{ routes.destinations.length }} current direct destinations</small></div>
    <RouterLink class="hero-globe-link" to="/explore">Explore routes <span aria-hidden="true">→</span></RouterLink>
  </div>
</template>

<style scoped>
.hero-globe { position: relative; min-height: 520px; }.hero-globe :deep(.globe-shell) { min-height: 520px; }.hero-globe-status { position: absolute; bottom: 48px; left: 12px; display: grid; max-width: 300px; gap: 3px; padding: 11px 13px; border: 1px solid rgba(255,255,255,.9); border-radius: 12px; background: rgba(255,255,255,.86); color: var(--ink-strong); box-shadow: var(--shadow-sm); backdrop-filter: blur(12px); }.hero-globe-status small { color: var(--muted); }.hero-globe-status.failed { border-color: rgba(217, 139, 0, .3); }.hero-globe-caption { position: absolute; bottom: 44px; left: 12px; display: grid; gap: 2px; padding: 12px 14px; border: 1px solid rgba(255,255,255,.9); border-radius: 13px; background: rgba(255,255,255,.88); box-shadow: var(--shadow-md); backdrop-filter: blur(12px); }.hero-globe-caption span { color: var(--brand); font-size: .68rem; font-weight: 800; letter-spacing: .08em; text-transform: uppercase; }.hero-globe-caption small { color: var(--muted); }.hero-globe-link { position: absolute; right: 12px; bottom: 12px; border-radius: 10px; padding: 10px 13px; background: var(--ink-strong); color: white; font-size: .82rem; font-weight: 760; text-decoration: none; }
@media (max-width: 680px) { .hero-globe, .hero-globe :deep(.globe-shell) { min-height: 410px; }.hero-globe-caption, .hero-globe-status { bottom: 54px; } }
</style>
