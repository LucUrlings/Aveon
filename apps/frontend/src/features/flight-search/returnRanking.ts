import type { SearchResult } from './types'

export type ReturnRanking = 'best' | 'cheapest' | 'fastest'

export const returnRankingOptions: Array<{ value: ReturnRanking; label: string }> = [
  { value: 'best', label: 'Recommended' },
  { value: 'cheapest', label: 'Cheapest' },
  { value: 'fastest', label: 'Fastest' },
]

const getPrice = (result: SearchResult) => result.priceOptions[0]?.totalPrice.amount ?? Number.POSITIVE_INFINITY
const getStopCount = (result: SearchResult) => result.legs.reduce(
  (count, leg) => count + Math.max(leg.segments.length - 1, 0),
  0,
)

const byPriceThenDuration = (left: SearchResult, right: SearchResult) =>
  getPrice(left) - getPrice(right) || left.totalDurationMinutes - right.totalDurationMinutes

export const rankReturnOptions = (results: SearchResult[], ranking: ReturnRanking) => {
  const ranked = [...results]

  if (ranking === 'cheapest') return ranked.sort(byPriceThenDuration)
  if (ranking === 'fastest') {
    return ranked.sort((left, right) =>
      left.totalDurationMinutes - right.totalDurationMinutes || byPriceThenDuration(left, right))
  }

  const fastestDuration = Math.min(...ranked.map((result) => result.totalDurationMinutes))
  const lowestPrice = Math.min(...ranked.map(getPrice))
  // Treat an extra hour as meaningful, but scale the penalty to the price range of the trip.
  // This prevents a small saving from promoting a dramatically longer itinerary.
  const timeValuePerHour = Math.min(30, Math.max(10, lowestPrice * 0.1))

  const score = (result: SearchResult) =>
    getPrice(result) +
    Math.max(0, result.totalDurationMinutes - fastestDuration) / 60 * timeValuePerHour +
    getStopCount(result) * timeValuePerHour * 0.75

  return ranked.sort((left, right) =>
    score(left) - score(right) || byPriceThenDuration(left, right))
}
