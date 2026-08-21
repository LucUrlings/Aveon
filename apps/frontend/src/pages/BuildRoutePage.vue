<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import OrderedRouteSearch from '../features/itinerary-search/OrderedRouteSearch.vue'

const route = useRoute()
const prefillRoute = computed(() => route.query.prefill === 'true' && typeof route.query.route === 'string'
  ? route.query.route.split(',').map(code => code.trim().toUpperCase()).filter(code => /^[A-Z]{3,4}$/.test(code))
  : [])
const prefillDepartureDate = computed(() => route.query.source === 'explore' && typeof route.query.departureDate === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(route.query.departureDate)
  ? route.query.departureDate
  : '')
const exploreHandoff = computed(() => route.query.source === 'explore' && prefillRoute.value.length > 1)
</script>

<template>
  <main id="main-content" class="advanced-page" tabindex="-1">
    <header>
      <p class="eyebrow">Build my route</p>
      <h1>Build an exact multi-destination route</h1>
      <p>Add each flight in the order you want to travel, choose the dates, and use multiple airport options wherever you need flexibility.</p>
      <p class="scope-note">Current results use separate one-way bookings. Each leg is searched independently, so review connection time and booking conditions before you buy.</p>
    </header>

    <section class="advanced-card" aria-label="Build my route search">
      <OrderedRouteSearch :prefill-route="prefillRoute" :prefill-departure-date="prefillDepartureDate" :explore-handoff="exploreHandoff" />
    </section>
  </main>
</template>

<style scoped src="./MultiDestinationPage.css"></style>
