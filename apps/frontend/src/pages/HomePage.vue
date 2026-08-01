<template>
  <main id="main-content" class="home-page" tabindex="-1">
    <section class="home-hero" aria-labelledby="home-title">
      <div class="hero-copy">
        <p class="home-eyebrow">Flight discovery, widened</p>
        <h1 id="home-title">Search the journey you mean, not just one route.</h1>
        <p class="hero-lead">Compare flexible airports and dates, build an exact sequence of flights, or let Aveon explore a bounded multi-destination trip. Useful results arrive progressively and every booking stays transparent.</p>
        <div class="hero-actions">
          <RouterLink class="primary-action" to="/search">Search flights</RouterLink>
          <RouterLink class="secondary-action" to="/multi-destination">Plan multiple destinations</RouterLink>
        </div>
        <ul class="hero-proof" aria-label="Aveon product principles">
          <li><strong>Flexible</strong><span>Airports and dates</span></li>
          <li><strong>Progressive</strong><span>Results as they arrive</span></li>
          <li><strong>Transparent</strong><span>Coverage and booking risks</span></li>
        </ul>
      </div>

      <div class="journey-visual" aria-label="Example flexible journey from Dublin through Amsterdam and Paris">
        <div class="visual-orbit visual-orbit--one"></div>
        <div class="visual-orbit visual-orbit--two"></div>
        <article class="journey-card journey-card--start"><span>Start group</span><strong>Dublin</strong><small>DUB · SNN · ORK</small></article>
        <div class="journey-line journey-line--one" aria-hidden="true"></div>
        <article class="journey-card journey-card--middle"><span>Flexible stop</span><strong>Amsterdam</strong><small>Stay 2+ nights</small></article>
        <div class="journey-line journey-line--two" aria-hidden="true"></div>
        <article class="journey-card journey-card--end"><span>Next destination</span><strong>Paris</strong><small>CDG · ORY</small></article>
      </div>
    </section>

    <section class="search-modes" aria-labelledby="search-modes-title">
      <div class="section-heading">
        <p class="home-eyebrow">Choose your starting point</p>
        <h2 id="search-modes-title">Two ways to discover the right flight.</h2>
        <p>Use the focused search when you know the trip shape. Use multi-destination planning when the route itself is part of the question.</p>
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
        <article class="mode-card mode-card--multi">
          <div class="mode-icon" aria-hidden="true">⌁</div>
          <p class="mode-kicker">Ordered or optimized</p>
          <h3>Plan multiple destinations</h3>
          <p>Build exact dated legs or describe destinations and stay rules so Aveon can compare complete journeys within clear limits.</p>
          <ul><li>Multiple airports per stop</li><li>Stay and endpoint rules</li><li>Transparent bounded coverage</li></ul>
          <RouterLink to="/multi-destination">Open trip planner <span aria-hidden="true">→</span></RouterLink>
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
