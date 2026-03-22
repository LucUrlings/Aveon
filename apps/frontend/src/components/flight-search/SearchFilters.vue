<script setup lang="ts">
defineProps<{
  providerFilters: string[]
}>()

const maxPrice = defineModel<number>('maxPrice', { required: true })
const directFlightsOnly = defineModel<boolean>('directFlightsOnly', { required: true })
const selectedProviders = defineModel<string[]>('selectedProviders', { required: true })
</script>

<template>
  <aside class="filters-panel">
    <div class="filters-card">
      <p class="eyebrow">Filters</p>
      <h3>Refine results</h3>

      <label class="filter-row">
        <span>Max price</span>
        <input v-model.number="maxPrice" type="range" min="50" max="1500" step="10" />
        <strong>EUR {{ maxPrice }}</strong>
      </label>

      <label class="filter-toggle">
        <input v-model="directFlightsOnly" type="checkbox" />
        <span>Direct flights</span>
      </label>

      <div v-if="providerFilters.length" class="provider-filter-group">
        <span class="filter-label">Preferred providers</span>
        <label
          v-for="provider in providerFilters"
          :key="provider"
          class="filter-toggle"
        >
          <input v-model="selectedProviders" :value="provider" type="checkbox" />
          <span>{{ provider.replace('FlightApi:', '') }}</span>
        </label>
      </div>
    </div>
  </aside>
</template>

<style scoped>
.filters-panel {
  position: sticky;
  top: 20px;
}

.filters-card {
  border: 1px solid rgba(29, 34, 40, 0.08);
  border-radius: 28px;
  background: rgba(255, 255, 255, 0.88);
  box-shadow: 0 24px 60px rgba(41, 49, 61, 0.08);
  backdrop-filter: blur(18px);
  padding: 22px 18px;
}

.eyebrow {
  margin: 0 0 10px;
  font-size: 0.78rem;
  font-weight: 700;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: #9c5a11;
}

h3 {
  margin: 0 0 18px;
  color: #1d2228;
  font-size: 1.25rem;
}

.filter-row {
  display: grid;
  gap: 10px;
  margin-bottom: 18px;
}

.filter-toggle {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 10px;
}

.filter-label,
.filter-row span {
  font-size: 0.82rem;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: #5b6570;
}

.provider-filter-group {
  padding-top: 14px;
  border-top: 1px solid rgba(29, 34, 40, 0.08);
}
</style>
