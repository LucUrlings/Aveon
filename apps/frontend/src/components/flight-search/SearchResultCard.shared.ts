import type { SearchResult } from '../../features/flight-search/types'

export type FareDifferenceBadgeTone =
  'shortConnection' |
  'longConnection' |
  'overnight' |
  'sellers'

export type FareDifferenceBadge = {
  label: string
  tone: FareDifferenceBadgeTone
}

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

const formatDate = (value: string) => {
  const formatted = formatDateTime(value)
  return formatted.replace(/\s\d{2}:\d{2}$/, '')
}

const formatTime = (value: string) => {
  const match = value.match(/T(\d{2}:\d{2})/)
  return match?.[1] ?? value
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

const getLayoverMinutes = (arrivalLocalTime: string, departureLocalTime: string) => {
  const arrival = new Date(arrivalLocalTime).getTime()
  const departure = new Date(departureLocalTime).getTime()
  const minutes = Math.round((departure - arrival) / 60000)
  return Number.isFinite(minutes) && minutes > 0 ? minutes : null
}

const getConnectionSummaries = (result: SearchResult) =>
  result.legs.flatMap((leg) =>
    leg.segments.slice(0, -1).map((segment, index) => {
      const nextSegment = leg.segments[index + 1]
      const layoverMinutes = nextSegment
        ? getLayoverMinutes(segment.arrivalLocalTime, nextSegment.departureLocalTime)
        : null

      return {
        airport: segment.destinationAirport,
        layoverMinutes,
      }
    }),
  )

const hasOvernightLeg = (result: SearchResult) =>
  result.legs.some((leg) => leg.departureLocalTime.slice(0, 10) !== leg.arrivalLocalTime.slice(0, 10))

const getConnectionSummary = (result: SearchResult) => {
  const connections = getConnectionSummaries(result)

  if (connections.length === 0) {
    return 'Direct'
  }

  return connections
    .map((connection) => connection.layoverMinutes
      ? `via ${connection.airport}, ${formatDuration(connection.layoverMinutes)}`
      : `via ${connection.airport}`)
    .join(' + ')
}

export const getFareDifferenceBadges = (result: SearchResult): FareDifferenceBadge[] => {
  const badges: FareDifferenceBadge[] = []
  const connections = getConnectionSummaries(result)

  const shortestLayoverMinutes = Math.min(
    ...connections
      .map((connection) => connection.layoverMinutes)
      .filter((minutes): minutes is number => minutes !== null),
  )
  const longestLayoverMinutes = Math.max(
    ...connections
      .map((connection) => connection.layoverMinutes)
      .filter((minutes): minutes is number => minutes !== null),
  )

  if (Number.isFinite(shortestLayoverMinutes) && shortestLayoverMinutes < 75) {
    badges.push({ label: 'Short connection', tone: 'shortConnection' })
  }

  if (Number.isFinite(longestLayoverMinutes) && longestLayoverMinutes >= 240) {
    badges.push({ label: 'Long layover', tone: 'longConnection' })
  }

  if (hasOvernightLeg(result)) {
    badges.push({ label: 'Overnight', tone: 'overnight' })
  }

  if (result.priceOptions.length > 1) {
    badges.push({
      label: `${result.priceOptions.length} sellers`,
      tone: 'sellers',
    })
  }

  return badges
}

export const getFareIdentityChips = (result: SearchResult) => {
  const firstLeg = result.legs[0]
  const lastLeg = result.legs[result.legs.length - 1]
  if (!firstLeg || !lastLeg) {
    return []
  }

  const dateChips = result.isRoundTrip
    ? [
        `Out ${formatDate(firstLeg.departureLocalTime)}`,
        `Return ${formatDate(lastLeg.departureLocalTime)}`,
      ]
    : [formatDate(firstLeg.departureLocalTime)]

  return [
    ...dateChips,
    formatTime(firstLeg.departureLocalTime),
    getConnectionSummary(result),
    formatDuration(result.totalDurationMinutes),
  ]
}

export const getPrimaryBookingLink = (result: SearchResult) => result.priceOptions[0].bookingLinks[0] ?? null

export const isDirectFlight = (result: SearchResult) =>
  result.legs.every((leg) => leg.segments.length === 1)

const isCombinedOneWayProvider = (provider: string) =>
  provider.includes('Combined one-way')

export const isSyntheticReturnFare = (result: SearchResult) =>
  result.isRoundTrip && (
    isCombinedOneWayProvider(result.priceOptions[0]?.provider ?? '') ||
    (result.priceOptions[0]?.bookingLinks.length ?? 0) > 1
  )

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
