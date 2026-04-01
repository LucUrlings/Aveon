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

export const formatPrice = (amount: number, currency: string) => `${currency} ${amount.toFixed(2)}`

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

export const formatResultForShare = (result: SearchResult, index: number) => {
  const primaryOption = result.priceOptions[0]
  const legLines = result.legs.map((leg, legIndex) => {
    const legLabel = result.isRoundTrip ? `${legIndex === 0 ? 'Outbound' : 'Return'}: ` : ''
    return `${legLabel}${leg.originAirport} -> ${leg.destinationAirport} | ${formatDateTime(leg.departureLocalTime)} to ${formatDateTime(leg.arrivalLocalTime)} | ${formatDuration(leg.durationMinutes)}`
  })
  const linkLines = primaryOption.bookingLinks
    .filter((link) => link.url)
    .map((link) => `${link.label || 'View fare'}: ${link.url}`)

  return [
    `${index}. ${result.isRoundTrip ? 'Round trip' : 'One-way'} | ${getAirlineSummary(result)}`,
    ...legLines,
    `Total: ${formatDuration(result.totalDurationMinutes)}`,
    `Fare: ${formatPrice(primaryOption.totalPrice.amount, primaryOption.totalPrice.currency)} via ${formatProviderName(primaryOption.provider)}`,
    ...linkLines,
  ].join('\n')
}
