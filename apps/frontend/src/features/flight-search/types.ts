import type { components } from '../../api/generated'

type GeneratedSearchRequest = components['schemas']['SearchRequest']
type GeneratedSearchSessionResponse = components['schemas']['SearchSessionResponse']

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

export type SearchSessionResponse = {
  searchId: NonNullable<GeneratedSearchSessionResponse['searchId']>
  status: NonNullable<GeneratedSearchSessionResponse['status']>
  totalCombinations: NonNullable<GeneratedSearchSessionResponse['totalCombinations']>
  completedCombinations: NonNullable<GeneratedSearchSessionResponse['completedCombinations']>
  failedCombinations: NonNullable<GeneratedSearchSessionResponse['failedCombinations']>
  response: SearchResponse
  errorMessage: GeneratedSearchSessionResponse['errorMessage']
}

export type SearchRequest = {
  originAirports: NonNullable<GeneratedSearchRequest['originAirports']>
  destinationAirports: NonNullable<GeneratedSearchRequest['destinationAirports']>
  departDateFrom: NonNullable<GeneratedSearchRequest['departDateFrom']>
  departDateTo: NonNullable<GeneratedSearchRequest['departDateTo']>
  returnDateFrom: GeneratedSearchRequest['returnDateFrom']
  returnDateTo: GeneratedSearchRequest['returnDateTo']
  adults: NonNullable<GeneratedSearchRequest['adults']>
  cabinClass: NonNullable<GeneratedSearchRequest['cabinClass']>
}

export const cabinOptions = [
  { label: 'Economy', value: 'economy' },
  { label: 'Business', value: 'business' },
  { label: 'First', value: 'first' },
  { label: 'Premium Economy', value: 'premium_economy' },
]
