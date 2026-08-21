<script setup lang="ts">
import OptimizedTripSearch from '../features/itinerary-search/OptimizedTripSearch.vue'
</script>

<template>
  <main id="main-content" class="advanced-page" tabindex="-1">
    <header>
      <p class="eyebrow">Optimize my trip</p>
      <h1>Compare complete multi-destination journeys</h1>
      <p>Choose the places you want to visit and your stay rules, then let Aveon compare destination orders and complete trips.</p>
      <p class="scope-note">Optimization is bounded by a transparent search allowance. It shows the best complete trips found within that allowance and cannot guarantee the global cheapest route. Flights are booked separately.</p>
    </header>

    <section class="advanced-card" aria-label="Optimize my trip search">
      <OptimizedTripSearch />
    </section>
  </main>
</template>

<style scoped src="./MultiDestinationPage.css"></style>
