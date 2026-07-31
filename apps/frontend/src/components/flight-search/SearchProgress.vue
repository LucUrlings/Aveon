<script setup lang="ts">
import { computed } from 'vue'
import type { SearchSessionResponse } from '../../features/flight-search/types'

const props = defineProps<{
  session: SearchSessionResponse
}>()

const progressPercentage = computed(() => {
  if (props.session.totalCombinations === 0) {
    return 0
  }

  return Math.round((props.session.completedCombinations / props.session.totalCombinations) * 100)
})
</script>

<template>
  <section class="progress-shell" aria-label="Flight search progress">
    <div class="progress-copy">
      <p class="eyebrow">Search Progress</p>
      <strong>
        {{ session.completedCombinations }} / {{ session.totalCombinations }} combinations
      </strong>
      <span v-if="session.failedCombinations > 0">
        {{ session.failedCombinations }} failed
      </span>
    </div>
    <div
      class="progress-bar"
      role="progressbar"
      aria-label="Completed search combinations"
      aria-valuemin="0"
      :aria-valuemax="session.totalCombinations"
      :aria-valuenow="session.completedCombinations"
      :aria-valuetext="`${session.completedCombinations} of ${session.totalCombinations} combinations complete`"
    >
      <div class="progress-bar-fill" :style="{ width: `${progressPercentage}%` }" />
    </div>
  </section>
</template>

<style scoped>
.progress-shell {
  width: min(1480px, 100%);
  margin: 0 auto 10px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface-raised);
  box-shadow: var(--shadow-sm);
}

.progress-copy {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}

.eyebrow {
  margin: 0;
  font-size: 0.7rem;
  font-weight: 800;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--brand);
}

.progress-copy strong {
  font-size: 0.95rem;
}

.progress-copy span {
  color: var(--muted);
}

.progress-bar {
  height: 8px;
  border-radius: 999px;
  background: #e8ecf4;
  overflow: hidden;
}

.progress-bar-fill {
  height: 100%;
  border-radius: 999px;
  background: linear-gradient(90deg, var(--brand) 0%, var(--accent) 100%);
  transition: width 0.4s ease;
}

@media (max-width: 640px) {
  .progress-shell {
    padding-left: 10px;
    padding-right: 10px;
    border-radius: 10px;
  }

  .progress-copy {
    gap: 6px;
    margin-bottom: 8px;
  }

  .progress-copy strong {
    font-size: 0.88rem;
  }
}
</style>
