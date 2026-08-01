<script setup lang="ts">
import AppNavbar from './components/AppNavbar.vue'
</script>

<template>
  <div class="app-shell">
    <a class="skip-link" href="#main-content">Skip to main content</a>
    <AppNavbar />
    <RouterView />

    <footer class="app-footer">
      <div class="app-footer-inner">
        <RouterLink class="footer-brand" to="/" aria-label="Aveon home">
          <span class="footer-brand-mark">A</span>
          <span>Aveon</span>
        </RouterLink>
        <p>Flexible flight discovery across airports and dates.</p>
        <nav aria-label="Footer navigation">
          <RouterLink to="/search">Search</RouterLink>
          <RouterLink to="/how-it-works">How it works</RouterLink>
          <RouterLink to="/about">About</RouterLink>
          <a href="https://github.com/LucUrlings/Aveon" target="_blank" rel="noreferrer">GitHub</a>
          <a href="https://lucurlings.nl" target="_blank" rel="noreferrer">Luc Urlings</a>
        </nav>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.skip-link { position: fixed; z-index: 1000; top: 8px; left: 8px; transform: translateY(-150%); border-radius: 8px; background: var(--ink-strong); color: #fff; padding: 10px 14px; font-weight: 750; text-decoration: none; }
.skip-link:focus { transform: translateY(0); }
.app-shell { min-height: 100vh; display: flex; flex-direction: column; }
.app-footer { margin-top: auto; border-top: 1px solid var(--border); background: rgba(255, 255, 255, 0.78); }
.app-footer-inner { width: min(1480px, 100%); margin: 0 auto; padding: 24px; display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 18px 30px; color: var(--muted); font-size: 0.82rem; }
.app-footer-inner p { margin: 0; }
.footer-brand { display: inline-flex; align-items: center; gap: 8px; color: var(--ink-strong); font-weight: 800; text-decoration: none; }
.footer-brand-mark { display: grid; width: 28px; height: 28px; place-items: center; border-radius: 9px; background: var(--ink-strong); color: #fff; font-size: 0.76rem; }
.app-footer nav { display: flex; flex-wrap: wrap; justify-content: end; gap: 10px 18px; }
.app-footer nav a { color: var(--muted); font-weight: 650; text-decoration: none; }
.app-footer nav a:hover { color: var(--brand-strong); }
@media (max-width: 720px) {
  .app-footer-inner { grid-template-columns: 1fr; padding: 22px 16px; }
  .app-footer nav { justify-content: start; }
}
</style>
