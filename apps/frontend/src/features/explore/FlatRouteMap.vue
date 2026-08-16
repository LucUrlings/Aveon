<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { geoNaturalEarth1, geoPath } from 'd3-geo'
import { feature } from 'topojson-client'
import countriesTopology from 'world-atlas/countries-110m.json'
import type { ExploreAirport, ExploreRoutesResponse } from './types'

const props = withDefaults(defineProps<{
  routes?: ExploreRoutesResponse | null
  selectedDestination?: ExploreAirport | null
}>(), {
  routes: null,
  selectedDestination: null,
})

const emit = defineEmits<{ select: [airport: ExploreAirport] }>()
const host = ref<HTMLElement | null>(null)
const width = ref(760)
const height = ref(440)
let resizeObserver: ResizeObserver | null = null

const countryCollection = feature(countriesTopology as any, countriesTopology.objects.countries as any)
const countries = ('features' in countryCollection ? countryCollection.features : [countryCollection]) as object[]
const projection = computed(() => {
  const value = geoNaturalEarth1().fitExtent(
    [[18, 18], [Math.max(width.value - 18, 20), Math.max(height.value - 18, 20)]],
    { type: 'Sphere' } as any,
  )
  const [translateX, translateY] = value.translate()
  return value.scale(value.scale() * 1.16).translate([translateX, translateY + height.value * .065])
})
const path = computed(() => geoPath(projection.value))
const countryPaths = computed(() => countries.map(country => path.value(country as any)).filter((value): value is string => Boolean(value)))

const sampleDestinations = (destinations: ExploreAirport[], limit = 30) => {
  if (destinations.length <= limit) return destinations
  return Array.from({ length: limit }, (_, index) => destinations[Math.round(index * (destinations.length - 1) / (limit - 1))])
}

const visibleDestinations = computed(() => sampleDestinations(props.routes?.destinations ?? []))
const originPoint = computed(() => {
  if (!props.routes) return null
  const [x, y] = projection.value([props.routes.origin.longitude, props.routes.origin.latitude]) ?? [0, 0]
  return { airport: props.routes.origin, x, y }
})
const destinationPoints = computed(() => {
  const routes = props.routes
  if (!routes) return []
  const labelStep = Math.max(Math.ceil(visibleDestinations.value.length / 16), 1)
  return visibleDestinations.value.map((airport, index) => {
    const [x, y] = projection.value([airport.longitude, airport.latitude]) ?? [0, 0]
    const routePath = path.value({
      type: 'LineString',
      coordinates: [
        [routes.origin.longitude, routes.origin.latitude],
        [airport.longitude, airport.latitude],
      ],
    } as any) ?? ''
    const labelOnLeft = x > width.value * .72
    const showLabel = visibleDestinations.value.length <= 18 || index % labelStep === 0 || airport.code === props.selectedDestination?.code
    return { airport, x, y, routePath, labelX: labelOnLeft ? -8 : 8, labelAnchor: labelOnLeft ? 'end' : 'start', showLabel }
  })
})

const resize = () => {
  if (!host.value) return
  width.value = Math.max(host.value.clientWidth, 320)
  height.value = Math.max(host.value.clientHeight, 320)
}

onMounted(() => {
  resize()
  if ('ResizeObserver' in window) {
    resizeObserver = new ResizeObserver(resize)
    resizeObserver.observe(host.value!)
  }
})

onBeforeUnmount(() => resizeObserver?.disconnect())
</script>

<template>
  <div ref="host" class="flat-route-map">
    <svg :viewBox="`0 0 ${width} ${height}`" role="img" :aria-label="routes ? `Direct routes from ${routes.origin.name}` : 'World route map'">
      <path v-for="(countryPath, index) in countryPaths" :key="index" class="map-country" :d="countryPath" />
      <path
        v-for="point in destinationPoints"
        :key="`route-${point.airport.code}`"
        class="map-route"
        :class="{ 'map-route--selected': selectedDestination?.code === point.airport.code }"
        :d="point.routePath"
      />
      <g
        v-for="point in destinationPoints"
        :key="point.airport.code"
        class="map-destination"
        :class="{ 'map-destination--selected': selectedDestination?.code === point.airport.code }"
        :transform="`translate(${point.x} ${point.y})`"
        role="button"
        tabindex="0"
        :aria-label="`Choose ${point.airport.name} in ${point.airport.city}`"
        :aria-pressed="selectedDestination?.code === point.airport.code"
        @click="emit('select', point.airport)"
        @keydown.enter.prevent="emit('select', point.airport)"
        @keydown.space.prevent="emit('select', point.airport)"
      >
        <circle r="4" />
        <text v-if="point.showLabel" :x="point.labelX" y="-7" :text-anchor="point.labelAnchor">{{ point.airport.city }}</text>
      </g>
      <g v-if="originPoint" class="map-origin" :transform="`translate(${originPoint.x} ${originPoint.y})`">
        <circle r="6" />
        <text x="10" y="14">{{ originPoint.airport.city }}</text>
      </g>
    </svg>
  </div>
</template>

<style scoped>
.flat-route-map { position: relative; width: 100%; height: 100%; min-height: 540px; overflow: hidden; border: 1px solid rgba(99,102,241,.16); border-radius: 26px; background: radial-gradient(circle at 54% 38%, rgba(99,102,241,.15), transparent 42%), linear-gradient(145deg, #f8faff, #e9efff); box-shadow: inset 0 1px rgba(255,255,255,.9); }
.flat-route-map::before { position: absolute; inset: 0; background-image: radial-gradient(rgba(79,70,229,.1) .7px, transparent .7px); background-size: 17px 17px; content: ''; opacity: .42; pointer-events: none; }
svg { position: relative; z-index: 1; display: block; width: 100%; height: 100%; }
.map-country { fill: rgba(177,190,221,.55); stroke: rgba(255,255,255,.9); stroke-width: .8; vector-effect: non-scaling-stroke; }
.map-route { fill: none; stroke: rgba(79,70,229,.3); stroke-width: 1.25; stroke-linecap: round; vector-effect: non-scaling-stroke; }
.map-route--selected { stroke: #f59e0b; stroke-width: 2.6; }
.map-destination { cursor: pointer; outline: none; }
.map-destination circle { fill: #22d3ee; stroke: #fff; stroke-width: 2; vector-effect: non-scaling-stroke; }
.map-destination text, .map-origin text { fill: #27314d; font-size: 10px; font-weight: 780; paint-order: stroke; stroke: rgba(248,250,255,.96); stroke-width: 3px; stroke-linejoin: round; }
.map-destination--selected circle { fill: #f59e0b; r: 5.5px; }
.map-destination:focus-visible circle { stroke: #4f46e5; stroke-width: 4; }
.map-origin { pointer-events: none; }.map-origin circle { fill: #4f46e5; stroke: #fff; stroke-width: 2.5; vector-effect: non-scaling-stroke; }.map-origin text { font-size: 11px; font-weight: 880; }
@media (max-width: 680px) { .flat-route-map { min-height: 410px; }.map-destination text { display: none; } }
</style>
