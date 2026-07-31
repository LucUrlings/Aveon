import { readonly, ref } from 'vue'

export const returnRankingOptions = [
  { value: 'best', label: 'Best value', description: 'Balances fare, journey time and stops.' },
  { value: 'cheapest', label: 'Cheapest', description: 'Always puts the lowest total fare first.' },
  { value: 'fastest', label: 'Fastest', description: 'Always puts the shortest journey first.' },
] as const

export type ReturnRanking = typeof returnRankingOptions[number]['value']

const storageKey = 'aveon.return-ranking'
const isReturnRanking = (value: string | null): value is ReturnRanking =>
  returnRankingOptions.some((option) => option.value === value)

const getStoredRanking = (): ReturnRanking => {
  if (typeof window === 'undefined') return 'best'

  const stored = window.localStorage.getItem(storageKey)
  return isReturnRanking(stored) ? stored : 'best'
}

const returnRanking = ref<ReturnRanking>(getStoredRanking())

const setReturnRanking = (value: ReturnRanking) => {
  returnRanking.value = value
  if (typeof window !== 'undefined') {
    window.localStorage.setItem(storageKey, value)
  }
}

const applyAccountReturnRanking = (value: ReturnRanking | null) => {
  if (value) {
    returnRanking.value = value
  }
}

export const useSearchPreferences = () => ({
  returnRanking: readonly(returnRanking),
  setReturnRanking,
  applyAccountReturnRanking,
})
