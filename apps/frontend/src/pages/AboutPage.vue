<script setup lang="ts">
const githubUrl = 'https://github.com/LucUrlings/Aveon'
const websiteUrl = 'https://lucurlings.nl'
</script>

<template>
  <main id="main-content" class="about-page" tabindex="-1">
    <section class="about-hero">
      <div class="about-hero-copy">
        <p class="about-eyebrow">About Aveon</p>
        <h1>The best flight may be the one you weren’t going to search for.</h1>
        <p>
          It might leave from the airport an hour away, depart a day earlier, or pair with a return another
          search engine never showed you. Aveon searches beyond the obvious route and turns that flexibility
          into journeys you can genuinely compare.
        </p>
        <div class="about-actions">
          <RouterLink class="about-primary-action" to="/">Start searching</RouterLink>
          <a class="about-secondary-action" :href="githubUrl" target="_blank" rel="noreferrer">View the source <span aria-hidden="true">↗</span></a>
        </div>
      </div>

      <aside class="about-snapshot" aria-label="Aveon product summary">
        <div class="snapshot-orbit snapshot-orbit-one"></div>
        <div class="snapshot-orbit snapshot-orbit-two"></div>
        <div class="snapshot-card">
          <span>One search</span>
          <strong>Many possible journeys</strong>
          <ul>
            <li><span>Multiple airports</span><b>✓</b></li>
            <li><span>Flexible travel dates</span><b>✓</b></li>
            <li><span>Real + synthetic returns</span><b>✓</b></li>
            <li><span>Progressive results</span><b>✓</b></li>
            <li><span>Multi-destination routes</span><b>✓</b></li>
          </ul>
        </div>
      </aside>
    </section>

    <section class="about-section" aria-labelledby="how-it-works-title">
      <div class="about-section-heading">
        <p class="about-eyebrow">How it works</p>
        <h2 id="how-it-works-title">Broad search in. Useful choices out.</h2>
      </div>
      <div class="process-grid">
        <article><span class="process-number">01</span><h3>Describe your flexibility</h3><p>Choose several origin and destination airports, then select a date range or specific dates.</p></article>
        <article><span class="process-number">02</span><h3>Compare progressively</h3><p>Results arrive as providers finish, so useful flights appear without making the whole page wait.</p></article>
        <article><span class="process-number">03</span><h3>Build the right trip</h3><p>Select an outbound, compare compatible returns, or build and optimize a bounded multi-destination journey before continuing to each fare provider.</p></article>
      </div>
    </section>

    <section class="principles-section">
      <div>
        <p class="about-eyebrow">Product principles</p>
        <h2>Built around transparent exploration.</h2>
        <p class="principles-intro">Flight search becomes hard when every extra airport and date multiplies the possibilities. Aveon keeps that flexibility while deliberately avoiding a giant, opaque list of every possible round trip.</p>
      </div>
      <div class="principles-list">
        <article><span>01</span><div><h3>Choice without overload</h3><p>Outbound and return decisions are staged so the result set stays useful and responsive.</p></div></article>
        <article><span>02</span><div><h3>Transparent combinations</h3><p>Real provider returns and compatible synthetic combinations remain clearly identified.</p></div></article>
        <article><span>03</span><div><h3>Open by default</h3><p>The product and its implementation are available publicly for inspection and improvement.</p></div></article>
      </div>
    </section>

    <section class="creator-card">
      <div class="creator-monogram" aria-hidden="true">LU</div>
      <div>
        <p class="about-eyebrow">Designed and built by</p>
        <h2>Luc Urlings</h2>
        <p>Aveon is an independent product built to make complex flight discovery feel simple.</p>
      </div>
      <div class="creator-links">
        <a :href="websiteUrl" target="_blank" rel="noreferrer">Visit website <span aria-hidden="true">↗</span></a>
        <a :href="githubUrl" target="_blank" rel="noreferrer">GitHub repository <span aria-hidden="true">↗</span></a>
      </div>
    </section>
  </main>
</template>

<style scoped src="./AboutPage.css"></style>
