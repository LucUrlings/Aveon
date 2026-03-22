export type AirportOption = {
  code: string
  name: string | null
  displayLabel: string
}

export type SearchResultSegment = {
  marketingCarrierName: string
  marketingCarrierCode: string
  flightNumber: string
  originAirport: string
  destinationAirport: string
  departureUtc: string
  arrivalUtc: string
  durationMinutes: number
}

export type SearchResultLeg = {
  originAirport: string
  destinationAirport: string
  departureUtc: string
  arrivalUtc: string
  durationMinutes: number
  segments: SearchResultSegment[]
}

export type SearchResultPriceOption = {
  id: string
  provider: string
  totalPrice: {
    amount: number
    currency: string
  }
  deepLink: string
}

export type SearchResult = {
  id: string
  isRoundTrip: boolean
  legs: SearchResultLeg[]
  totalDurationMinutes: number
  priceOptions: SearchResultPriceOption[]
}

export type SearchMetadata = {
  searchCombinationCount: number
  providerResultCount: number
  returnedResultCount: number
  returnedDirectFlightCount: number
  returnedOneStopFlightCount: number
  returnedTwoPlusStopFlightCount: number
}

export type SearchResponse = {
  results: SearchResult[]
  metadata: SearchMetadata
}

export type SearchRequest = {
  originAirports: string[]
  destinationAirports: string[]
  departDateFrom: string
  departDateTo: string
  returnDateFrom: string | null
  returnDateTo: string | null
  adults: number
  cabinClass: string
}

export const cabinOptions = [
  { label: 'Economy', value: 'economy' },
  { label: 'Business', value: 'business' },
  { label: 'First', value: 'first' },
  { label: 'Premium Economy', value: 'premium_economy' },
]

export const flexibilityOptions = [
  { label: 'Exact', value: 0 },
  { label: '±1 day', value: 1 },
  { label: '±2 days', value: 2 },
  { label: '±3 days', value: 3 },
]
