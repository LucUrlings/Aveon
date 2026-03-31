import type { SearchResult } from '../../features/flight-search/types'

export const formatDateTime = (value: string) => {
  const match = value.match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/)
  if (!match) {
    return value
  }

  const [, year, month, day, hours, minutes] = match
  const date = new Date(Number.parseInt(year, 10), Number.parseInt(month, 10) - 1, Number.parseInt(day, 10))
  const weekday = new Intl.DateTimeFormat('en-IE', {
    weekday: 'short',
  }).format(date).slice(0, 2)
  const monthLabel = new Intl.DateTimeFormat('en-IE', {
    month: 'short',
  }).format(date)

  return `${weekday} ${day} ${monthLabel} ${hours}:${minutes}`
}

export const formatDuration = (totalMinutes: number) => {
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  return `${hours}h ${minutes}m`
}

export const formatProviderName = (provider: string) => provider.replace(/^FlightApi:/, '').trim()

export const getAirlineSummary = (result: SearchResult) => {
  const airlines = [...new Set(
    result.legs.flatMap((leg) =>
      leg.segments.map((segment) => segment.marketingCarrierName),
    ),
  )].filter(Boolean)

  return airlines.join(', ') || 'Unknown airline'
}

export const getPrimaryBookingLink = (result: SearchResult) => result.priceOptions[0].bookingLinks[0] ?? null

export const isDirectFlight = (result: SearchResult) =>
  result.legs.every((leg) => leg.segments.length === 1)

export const isSyntheticReturnFare = (result: SearchResult) =>
  result.isRoundTrip && (result.priceOptions[0]?.bookingLinks.length ?? 0) > 1

export const isActualReturnFare = (result: SearchResult) =>
  result.isRoundTrip && !isSyntheticReturnFare(result)
