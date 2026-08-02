<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { GlobeInstance } from 'globe.gl'
import { feature } from 'topojson-client'
import countriesTopology from 'world-atlas/countries-110m.json'
import type { ExploreAirport, ExploreRoutesResponse } from './types'

const props = withDefaults(defineProps<{
  routes?: ExploreRoutesResponse | null
  interactive?: boolean
  autoRotate?: boolean
  allowZoom?: boolean
  selectedDestination?: ExploreAirport | null
  hoveredDestination?: ExploreAirport | null
  committedPath?: ExploreAirport[]
}>(), {
  interactive: true,
  autoRotate: true,
  allowZoom: true,
  selectedDestination: null,
  hoveredDestination: null,
  committedPath: () => [],
})

const emit = defineEmits<{ select: [airport: ExploreAirport]; hover: [airport: ExploreAirport | null] }>()
const host = ref<HTMLElement | null>(null)
const unavailable = ref(false)
const ready = ref(false)
let globe: GlobeInstance | null = null
let resizeObserver: ResizeObserver | null = null
let reducedMotion: MediaQueryList | null = null

type GlobePoint = ExploreAirport & { origin: boolean; committed: boolean }
type GlobeArc = {
  startLat: number
  startLng: number
  endLat: number
  endLng: number
  destination: ExploreAirport
  committed: boolean
  layer: 'route' | 'animation'
}
const countryCollection = feature(countriesTopology as any, countriesTopology.objects.countries as any)
const countries = ('features' in countryCollection ? countryCollection.features : [countryCollection]) as object[]
const escapeHtml = (value: string) => value.replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]!)

const resize = () => {
  if (!globe || !host.value) return
  globe.width(Math.max(host.value.clientWidth, 280)).height(Math.max(host.value.clientHeight, 320))
}

const configureData = () => {
  if (!globe) return
  const routes = props.routes
  if (!routes) {
    globe.pointsData([]).arcsData([])
    return
  }
  const pointMap = new Map<string, GlobePoint>()
  props.committedPath.forEach((airport, index) => pointMap.set(airport.code, { ...airport, origin: index === props.committedPath.length - 1, committed: true }))
  pointMap.set(routes.origin.code, { ...routes.origin, origin: true, committed: true })
  routes.destinations.forEach(destination => pointMap.set(destination.code, { ...destination, origin: false, committed: pointMap.get(destination.code)?.committed ?? false }))
  const points = [...pointMap.values()]
  const routeArcs = routes.destinations.map<Omit<GlobeArc, 'layer'>>(destination => ({
    startLat: routes.origin.latitude,
    startLng: routes.origin.longitude,
    endLat: destination.latitude,
    endLng: destination.longitude,
    destination,
    committed: false,
  }))
  for (let index = 1; index < props.committedPath.length; index += 1) {
    const from = props.committedPath[index - 1]
    const destination = props.committedPath[index]
    routeArcs.push({ startLat: from.latitude, startLng: from.longitude, endLat: destination.latitude, endLng: destination.longitude, destination, committed: true })
  }
  const emphasizedCode = props.hoveredDestination?.code ?? props.selectedDestination?.code
  const arcs = routeArcs.flatMap<GlobeArc>(arc => {
    const isEmphasized = arc.committed || arc.destination.code === emphasizedCode
    return isEmphasized
      ? [{ ...arc, layer: 'route' }, { ...arc, layer: 'animation' }]
      : [{ ...arc, layer: 'animation' }]
  })
  globe
    .pointsData(points)
    .pointLat('latitude')
    .pointLng('longitude')
    .pointColor(point => {
      const airport = point as GlobePoint
      if (airport.code === props.selectedDestination?.code) return '#f59e0b'
      return airport.origin ? '#f59e0b' : airport.committed ? '#a78bfa' : '#22d3ee'
    })
    .pointRadius(point => (point as GlobePoint).origin || (point as GlobePoint).code === props.selectedDestination?.code ? .55 : .28)
    .pointAltitude(point => (point as GlobePoint).origin ? .025 : .012)
    .pointLabel(point => {
      const airport = point as GlobePoint
      return `<strong>${escapeHtml(airport.city)} (${escapeHtml(airport.code)})</strong><br>${escapeHtml(airport.name)}`
    })
    .arcsData(arcs)
    .arcStartLat('startLat')
    .arcStartLng('startLng')
    .arcEndLat('endLat')
    .arcEndLng('endLng')
    .arcColor((arc: object) => {
      const value = arc as GlobeArc
      if (value.layer === 'route') {
        return value.committed ? 'rgba(167,139,250,.28)' : 'rgba(245,158,11,.3)'
      }
      if (value.committed) return ['rgba(167,139,250,.88)', 'rgba(245,158,11,.95)']
      if (value.destination.code === emphasizedCode) return ['rgba(245,158,11,.8)', 'rgba(34,211,238,1)']
      if (emphasizedCode) return ['rgba(79,70,229,.05)', 'rgba(34,211,238,.08)']
      return ['rgba(79,70,229,.35)', 'rgba(34,211,238,.8)']
    })
    .arcStroke((arc: object) => {
      const value = arc as GlobeArc
      if (value.layer === 'route') return value.committed ? .52 : .6
      if (value.committed || value.destination.code === emphasizedCode) return .8
      return emphasizedCode ? .12 : .45
    })
    .arcAltitudeAutoScale(.22)
    .arcDashLength((arc: object) => reducedMotion?.matches || (arc as GlobeArc).layer === 'route' ? 1 : .45)
    .arcDashGap((arc: object) => reducedMotion?.matches || (arc as GlobeArc).layer === 'route' ? 0 : 1.4)
    .arcDashInitialGap(0)
    .arcDashAnimateTime((arc: object) => reducedMotion?.matches || (arc as GlobeArc).layer === 'route' ? 0 : 2000)
    .arcsTransitionDuration(0)
    .onPointClick(point => {
      const airport = point as GlobePoint
      if (props.interactive && !airport.origin) emit('select', airport)
    })
  const hoverableGlobe = globe as GlobeInstance & { onPointHover?: (handler: (point: object | null) => void) => GlobeInstance }
  hoverableGlobe.onPointHover?.(point => {
      const airport = point as GlobePoint | null
      emit('hover', airport && !airport.origin ? airport : null)
  })
}

const setRotation = () => {
  if (!globe) return
  const controls = globe.controls()
  controls.autoRotate = props.autoRotate && !Boolean(reducedMotion?.matches)
  controls.autoRotateSpeed = .35
  controls.enablePan = false
  controls.enableZoom = props.allowZoom
}

const pauseRotation = () => {
  if (globe) globe.controls().autoRotate = false
}

const focusDestination = (airport: ExploreAirport) => {
  globe?.pointOfView({ lat: airport.latitude, lng: airport.longitude, altitude: 1.65 }, 900)
}

const panToDestination = (airport: ExploreAirport) => {
  pauseRotation()
  globe?.pointOfView(
    { lat: airport.latitude, lng: airport.longitude, altitude: 1.65 },
    reducedMotion?.matches ? 0 : 450,
  )
}

defineExpose({ focusDestination })

onMounted(async () => {
  try {
    reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)')
    const { default: Globe } = await import('globe.gl')
    if (!host.value) return
    globe = new Globe(host.value, { animateIn: !reducedMotion.matches })
    globe
      .backgroundColor('rgba(0,0,0,0)')
      .showAtmosphere(true)
      .atmosphereColor('#818cf8')
      .atmosphereAltitude(.16)
      .showGraticules(true)
      .polygonsData(countries)
      .polygonAltitude(.006)
      .polygonCapColor(() => 'rgba(82, 111, 158, .78)')
      .polygonSideColor(() => 'rgba(38, 57, 94, .3)')
      .polygonStrokeColor(() => 'rgba(173, 216, 230, .42)')
      .pointOfView(props.routes
        ? { lat: props.routes.origin.latitude, lng: props.routes.origin.longitude, altitude: 2.05 }
        : { lat: 18, lng: 0, altitude: 2.15 })
    const material = globe.globeMaterial()
    material.color.set('#111a38')
    material.emissive.set('#080d20')
    material.emissiveIntensity = .45
    material.shininess = .6
    configureData()
    setRotation()
    await nextTick()
    resize()
    ready.value = true
    if ('ResizeObserver' in window) {
      resizeObserver = new ResizeObserver(resize)
      resizeObserver.observe(host.value)
    }
    host.value.addEventListener('pointerdown', pauseRotation)
    if (props.allowZoom) host.value.addEventListener('wheel', pauseRotation, { passive: true })
    host.value.addEventListener('pointerleave', setRotation)
  } catch {
    ready.value = false
    unavailable.value = true
  }
})

watch(() => props.routes, routes => {
  configureData()
  if (routes && globe) globe.pointOfView({ lat: routes.origin.latitude, lng: routes.origin.longitude, altitude: 2.05 }, 700)
}, { deep: true })
watch(() => [props.selectedDestination, props.committedPath], configureData, { deep: true })
watch(() => props.hoveredDestination, airport => {
  configureData()
  if (airport) panToDestination(airport)
  else setRotation()
}, { deep: true, flush: 'sync' })
watch(() => props.autoRotate, setRotation)
watch(() => props.allowZoom, setRotation)

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  host.value?.removeEventListener('pointerdown', pauseRotation)
  host.value?.removeEventListener('wheel', pauseRotation)
  host.value?.removeEventListener('pointerleave', setRotation)
  globe?._destructor()
  globe = null
})
</script>

<template>
  <div class="globe-shell">
    <div v-if="!unavailable" ref="host" class="globe-canvas" :class="{ 'globe-canvas--ready': ready }" aria-hidden="true" />
    <Transition name="globe-loading">
      <div v-if="!ready && !unavailable" class="globe-loading" role="status"><span aria-hidden="true" /><strong>Drawing route map…</strong></div>
    </Transition>
    <div v-if="unavailable" class="globe-fallback" role="img" :aria-label="routes ? `Routes from ${routes.origin.city}` : 'World route map'"><span>◎</span><strong>{{ routes?.origin.code ?? 'Explore' }}</strong><small>{{ routes ? `${routes.destinations.length} current direct destinations` : 'Loading the route map' }}</small></div>
  </div>
</template>

<style scoped>
.globe-shell, .globe-canvas { width: 100%; height: 100%; min-height: 360px; }.globe-shell { position: relative; overflow: hidden; border-radius: 26px; background: radial-gradient(circle at 50% 45%, rgba(79, 70, 229, .18), transparent 58%); }.globe-canvas { opacity: 0; transition: opacity .45s ease; }.globe-canvas--ready { opacity: 1; }.globe-canvas :deep(canvas) { display: block; cursor: grab; }.globe-canvas :deep(canvas:active) { cursor: grabbing; }.globe-loading { position: absolute; inset: 0; z-index: 2; display: grid; place-content: center; justify-items: center; gap: 12px; background: radial-gradient(circle at 50% 45%, rgba(79,70,229,.2), rgba(247,248,255,.94) 64%); color: var(--ink-strong); }.globe-loading span { width: 38px; height: 38px; border: 3px solid rgba(79,70,229,.18); border-top-color: var(--brand); border-radius: 50%; animation: globe-spin .8s linear infinite; }.globe-loading-enter-active, .globe-loading-leave-active { transition: opacity .4s ease; }.globe-loading-enter-from, .globe-loading-leave-to { opacity: 0; }.globe-fallback { display: grid; min-height: 360px; place-content: center; justify-items: center; gap: 8px; color: var(--muted); text-align: center; }.globe-fallback span { display: grid; width: 190px; height: 190px; place-items: center; border: 1px solid rgba(99, 102, 241, .3); border-radius: 50%; background: radial-gradient(circle at 35% 30%, #28336c, #10162f 68%); color: #67e8f9; font-size: 7rem; box-shadow: 0 0 50px rgba(79, 70, 229, .2); }.globe-fallback strong { color: var(--ink-strong); font-size: 1.25rem; }@keyframes globe-spin { to { transform: rotate(360deg); } }
@media (prefers-reduced-motion: reduce) { .globe-canvas, .globe-loading-enter-active, .globe-loading-leave-active { transition: none; }.globe-loading span { animation: none; } }
@media (max-width: 640px) { .globe-shell, .globe-canvas, .globe-fallback { min-height: 320px; } }
</style>
