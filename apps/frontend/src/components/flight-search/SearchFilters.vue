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
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.88);
  box-shadow: 0 24px 60px rgba(41, 49, 61, 0.08);
  backdrop-filter: blur(18px);
  padding: 12px;
}

.filters-card,
.filter-row,
.provider-filter-group,
.filter-toggle {
  min-width: 0;
}

.eyebrow {
  margin: 0 0 6px;
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: #9c5a11;
}

h3 {
  margin: 0 0 8px;
  color: #1d2228;
  font-size: 0.96rem;
  font-weight: 600;
}

.filter-row {
  display: grid;
  gap: 6px;
  margin-bottom: 10px;
}

.filter-toggle {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 6px;
  font-size: 0.88rem;
}

.filter-toggle span {
  min-width: 0;
  overflow-wrap: anywhere;
}

.filter-label,
.filter-row span {
  font-size: 0.74rem;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: #5b6570;
}

.provider-filter-group {
  padding-top: 8px;
  border-top: 1px solid rgba(29, 34, 40, 0.08);
}

@media (max-width: 960px) {
  .filters-panel {
    position: static;
  }
}

@media (max-width: 640px) {
  .filters-card {
    padding: 10px;
    border-radius: 10px;
  }

  h3 {
    font-size: 0.92rem;
  }

  .filter-toggle {
    font-size: 0.84rem;
  }

  .provider-filter-group {
    display: grid;
    gap: 2px;
  }
}
</style>
