<script setup lang="ts">
import HeroRouteGlobe from '../features/explore/HeroRouteGlobe.vue'
</script>

<template>
  <main id="main-content" class="home-page" tabindex="-1">
    <section class="home-hero" aria-labelledby="home-title">
      <div class="hero-copy">
        <p class="home-eyebrow">Flight discovery, widened</p>
        <h1 id="home-title">Compare flights across flexible airports, dates, and destinations.</h1>
        <p class="hero-lead">Compare flexible airports and dates, build an exact sequence of flights, or let Aveon explore a bounded multi-destination trip. Useful results arrive progressively and every booking stays transparent.</p>
        <div class="hero-actions">
          <RouterLink class="primary-action" to="/search">Search flights</RouterLink>
          <RouterLink class="secondary-action" to="/explore">Explore destinations</RouterLink>
          <RouterLink class="secondary-action" to="/build-route">Build a route</RouterLink>
        </div>
        <ul class="hero-proof" aria-label="Aveon product principles">
          <li><strong>Flexible</strong><span>Airports and dates</span></li>
          <li><strong>Progressive</strong><span>Results as they arrive</span></li>
          <li><strong>Transparent</strong><span>Coverage and booking risks</span></li>
        </ul>
      </div>

      <HeroRouteGlobe />
    </section>

    <section class="search-modes" aria-labelledby="search-modes-title">
      <div class="section-heading">
        <p class="home-eyebrow">Choose your starting point</p>
        <h2 id="search-modes-title">Four ways to discover the right flight.</h2>
        <p>Search a known route, explore where one airport can take you, build an exact itinerary, or ask Aveon to compare complete trips.</p>
      </div>
      <div class="mode-grid">
        <article class="mode-card mode-card--simple">
          <div class="mode-icon" aria-hidden="true">→</div>
          <p class="mode-kicker">One-way and return</p>
          <h3>Search flexible flights</h3>
          <p>Search several origin airports, destinations, and dates at once. Compare outbound options first, then see compatible returns.</p>
          <ul><li>Multiple airport groups</li><li>Flexible or selected dates</li><li>Complete filters and rankings</li></ul>
          <RouterLink to="/search">Open flight search <span aria-hidden="true">→</span></RouterLink>
        </article>
        <article class="mode-card mode-card--explore">
          <div class="mode-icon" aria-hidden="true">◎</div>
          <p class="mode-kicker">Start with possibility</p>
          <h3>Explore direct destinations</h3>
          <p>Pick one airport and see its current direct-flight network across an interactive globe.</p>
          <ul><li>Current scheduled routes</li><li>Random destination inspiration</li><li>Explicit fare and onward-route actions</li></ul>
          <RouterLink to="/explore">Open route explorer <span aria-hidden="true">→</span></RouterLink>
        </article>
        <article class="mode-card mode-card--build">
          <div class="mode-icon" aria-hidden="true">⌁</div>
          <p class="mode-kicker">Exact itinerary</p>
          <h3>Build my route</h3>
          <p>Add every flight in the order you want to travel, with dates and flexible airport choices for each leg.</p>
          <ul><li>Dated flight sequence</li><li>Multiple airports per leg</li><li>Optional return to the start</li></ul>
          <RouterLink to="/build-route">Open route builder <span aria-hidden="true">→</span></RouterLink>
        </article>
        <article class="mode-card mode-card--optimize">
          <div class="mode-icon" aria-hidden="true">◇</div>
          <p class="mode-kicker">Bounded comparison</p>
          <h3>Optimize my trip</h3>
          <p>Describe your destinations and stay rules so Aveon can compare destination orders and complete journeys within clear limits.</p>
          <ul><li>Flexible destination order</li><li>Stay and endpoint rules</li><li>Transparent bounded coverage</li></ul>
          <RouterLink to="/optimize-trip">Open trip optimizer <span aria-hidden="true">→</span></RouterLink>
        </article>
      </div>
    </section>

    <section class="home-explainer" aria-labelledby="explainer-title">
      <div><p class="home-eyebrow">Designed for honest comparison</p><h2 id="explainer-title">Aveon finds options. You stay in control.</h2></div>
      <div class="explainer-copy">
        <p>Aveon is a metasearch product, not a booking engine. Prices and availability come from flight providers, and each purchase is completed with them.</p>
        <p>Multi-destination results currently use separate one-way bookings. When an optimized search reaches a configured limit, Aveon labels its coverage as bounded rather than claiming it found the global cheapest route.</p>
        <RouterLink to="/how-it-works">See how the search works <span aria-hidden="true">→</span></RouterLink>
      </div>
    </section>
  </main>
</template>

<style scoped src="./HomePage.css"></style>
