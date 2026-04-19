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
  departureLocalTime: string
  arrivalLocalTime: string
  durationMinutes: number
}

export type SearchResultLeg = {
  originAirport: string
  destinationAirport: string
  departureLocalTime: string
  arrivalLocalTime: string
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
  bookingLinks: {
    label: string
    url: string
    price?: {
      amount: number
      currency: string
    } | null
  }[]
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

export type SearchFilterOptionCount = {
  value: string
  count: number
}

export type SearchRangeMetadata = {
  min: number
  max: number
}

export type SearchStopFilterMetadata = {
  direct: number
  oneStop: number
  twoPlusStop: number
}

export type SearchFiltersMetadata = {
  providers: SearchFilterOptionCount[]
  airlines: SearchFilterOptionCount[]
  departureAirports: SearchFilterOptionCount[]
  arrivalAirports: SearchFilterOptionCount[]
  durationMinutes: SearchRangeMetadata
  departureTimeMinutes: SearchRangeMetadata
  arrivalTimeMinutes: SearchRangeMetadata
  returnDepartureTimeMinutes: SearchRangeMetadata
  returnArrivalTimeMinutes: SearchRangeMetadata
  stops: SearchStopFilterMetadata
}

export type SearchPagination = {
  page: number
  pageSize: number
  totalResults: number
  totalPages: number
}

export type SearchResponse = {
  results: SearchResult[]
  metadata: SearchMetadata
  filters: SearchFiltersMetadata
  pagination: SearchPagination
}

export type SearchResultsQuery = {
  direct?: boolean
  oneStop?: boolean
  twoPlusStop?: boolean
  providers?: string[]
  airlines?: string[]
  departureAirports?: string[]
  arrivalAirports?: string[]
  maxDuration?: number
  departureTime?: [number, number]
  arrivalTime?: [number, number]
  returnDepartureTime?: [number, number]
  returnArrivalTime?: [number, number]
  page?: number
  pageSize?: number
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
  selectedDates: NonNullable<GeneratedSearchRequest['selectedDates']>
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
