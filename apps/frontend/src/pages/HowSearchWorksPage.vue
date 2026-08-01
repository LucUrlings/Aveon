<template>
  <main id="main-content" class="guide-page" tabindex="-1">
    <section class="guide-hero">
      <div>
        <p class="guide-eyebrow">Inside the search</p>
        <h1>From a flexible idea to a bookable journey.</h1>
        <p class="guide-lead">
          Aveon searches a wider space than a traditional flight form, without making you wait for every
          possible combination. Here is what happens after you press search.
        </p>
        <RouterLink class="guide-action" to="/">Try a search</RouterLink>
      </div>

      <div class="search-equation" aria-label="Search combination example">
        <div><strong>2</strong><span>origin airports</span></div>
        <b>×</b>
        <div><strong>3</strong><span>destinations</span></div>
        <b>×</b>
        <div><strong>4</strong><span>departure dates</span></div>
        <b>=</b>
        <div class="equation-result"><strong>24</strong><span>outbound searches</span></div>
      </div>
    </section>

    <section class="flow-section" aria-labelledby="flow-title">
      <div class="section-heading">
        <p class="guide-eyebrow">The search pipeline</p>
        <h2 id="flow-title">Useful results first, complete results afterwards.</h2>
        <p>A search session keeps the work moving on the server while your browser receives partial updates.</p>
      </div>

      <ol class="flow-list">
        <li>
          <span class="flow-number">01</span>
          <div class="flow-copy">
            <h3>A search plan is created</h3>
            <p>Duplicate airports are removed, impossible same-airport routes are skipped, and your airports and dates are expanded into provider calls. Searches that exceed the server safety limit are rejected before they can consume excessive memory.</p>
          </div>
          <span class="flow-tag">Plan</span>
        </li>
        <li>
          <span class="flow-number">02</span>
          <div class="flow-copy">
            <h3>Providers are queried with bounded concurrency</h3>
            <p>Only a controlled number of calls run at once. Cached provider responses are reused where possible, reducing duplicate work and protecting both the server and provider limits.</p>
          </div>
          <span class="flow-tag">Search</span>
        </li>
        <li>
          <span class="flow-number">03</span>
          <div class="flow-copy">
            <h3>Results arrive progressively</h3>
            <p>A search session is returned immediately. The page polls that session and adds normalized, grouped fares as individual calls finish, so you do not wait for the slowest combination.</p>
          </div>
          <span class="flow-tag">Stream</span>
        </li>
        <li>
          <span class="flow-number">04</span>
          <div class="flow-copy">
            <h3>You refine the shortlist</h3>
            <p>Provider fares for the same flight are grouped together. Stops, times, airlines, airports, duration and price can then be filtered without repeating the original search.</p>
          </div>
          <span class="flow-tag">Compare</span>
        </li>
      </ol>
    </section>

    <section class="return-section" aria-labelledby="return-title">
      <div class="return-intro">
        <p class="guide-eyebrow">Return journeys</p>
        <h2 id="return-title">Why outbound and return flights are selected in stages.</h2>
        <p>
          Combining every outbound with every return produces a rapidly growing result set. Aveon avoids
          materializing that entire cross-product: you choose an outbound first, then the server builds only
          the return options relevant to that choice.
        </p>
      </div>

      <div class="return-flow" aria-label="Return journey selection flow">
        <article>
          <span>Step 1</span>
          <div class="return-icon">→</div>
          <h3>Choose an outbound</h3>
          <p>Outbound options become usable as soon as they arrive.</p>
        </article>
        <div class="return-connector" aria-hidden="true">›</div>
        <article>
          <span>In the background</span>
          <div class="return-icon">↻</div>
          <h3>Returns keep loading</h3>
          <p>One provider slot is reserved for return recommendations while outbound searches continue.</p>
        </article>
        <div class="return-connector" aria-hidden="true">›</div>
        <article>
          <span>Step 2</span>
          <div class="return-icon">⇄</div>
          <h3>Compare compatible returns</h3>
          <p>Only journeys departing after your outbound arrives are shown.</p>
        </article>
      </div>
    </section>

    <section class="fare-types" aria-labelledby="fare-types-title">
      <div class="section-heading">
        <p class="guide-eyebrow">Understanding the options</p>
        <h2 id="fare-types-title">Real and synthetic returns stay distinct.</h2>
      </div>
      <div class="fare-type-grid">
        <article class="fare-type-card real">
          <span class="fare-badge">Provider round trip</span>
          <h3>Real combination</h3>
          <p>The provider returned the outbound and inbound together as a single round-trip fare.</p>
          <ul>
            <li>One combined provider quote</li>
            <li>One round-trip result</li>
            <li>Provider booking terms apply</li>
          </ul>
        </article>
        <article class="fare-type-card synthetic">
          <span class="fare-badge">Two one-way fares</span>
          <h3>Synthetic combination</h3>
          <p>Aveon pairs a bookable outbound with a compatible, separately bookable inbound.</p>
          <ul>
            <li>Total price is the sum of both fares</li>
            <li>Outbound and return are booked separately</li>
            <li>Each booking link remains visible</li>
          </ul>
        </article>
      </div>
    </section>

    <section class="fare-types" aria-labelledby="multi-destination-title">
      <div class="section-heading">
        <p class="guide-eyebrow">Multi-destination travel</p>
        <h2 id="multi-destination-title">Choose the route, or ask Aveon to compare it.</h2>
        <p><strong>Build my route</strong> searches the dated flight sequence you enter. <strong>Optimize my trip</strong> compares valid destination orders and stay schedules, returning complete itineraries progressively.</p>
        <p>The optimizer is deliberately bounded by provider calls, candidate states, retained results, and execution time. “Bounded” coverage means Aveon found useful complete trips within those limits and cannot guarantee the global cheapest route. Current multi-destination flights are booked separately.</p>
      </div>
    </section>

    <section class="guide-note">
      <div>
        <p class="guide-eyebrow">Good to know</p>
        <h2>Aveon helps you discover. It does not sell the ticket.</h2>
      </div>
      <p>Prices and availability originate with flight providers and can change before booking. Aveon sends you to the relevant provider to complete each purchase.</p>
      <RouterLink to="/">Search flights <span aria-hidden="true">→</span></RouterLink>
    </section>
  </main>
</template>

<style scoped src="./HowSearchWorksPage.css"></style>
