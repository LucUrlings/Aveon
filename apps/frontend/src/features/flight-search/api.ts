import type { components } from '../../api/generated'
import type {
  AirportOption,
  SearchMetadata,
  SearchRequest,
  SearchResponse,
  SearchSessionResponse,
  SearchResult,
  SearchResultLeg,
  SearchResultPriceOption,
  SearchResultSegment,
} from './types'

type ApiAirportLookupResponse = components['schemas']['AirportLookupResponse']
type ApiAirportOption = components['schemas']['AirportOption']
type ApiSearchRequest = components['schemas']['SearchRequest']
type ApiSearchResponse = components['schemas']['SearchResponse']
type ApiSearchMetadata = components['schemas']['SearchMetadata']
type ApiSearchResult = components['schemas']['SearchResult']
type ApiSearchResultLeg = components['schemas']['SearchResultLeg']
type ApiSearchResultPriceOption = components['schemas']['SearchResultPriceOption']
type ApiSearchResultSegment = components['schemas']['SearchResultSegment']
type ApiSearchSessionResponse = components['schemas']['SearchSessionResponse']

const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim()
const apiBaseUrl = configuredApiBaseUrl
  ? configuredApiBaseUrl.replace(/\/$/, '')
  : import.meta.env.DEV
    ? 'http://localhost:5200'
    : ''

const normalizeAirportOption = (airport: ApiAirportOption): AirportOption => ({
  code: airport.code ?? '',
  name: airport.name ?? null,
  displayLabel: airport.displayLabel ?? airport.code ?? '',
})

const normalizeSegment = (segment: ApiSearchResultSegment): SearchResultSegment => ({
  marketingCarrierName: segment.marketingCarrierName ?? 'Unknown airline',
  marketingCarrierCode: segment.marketingCarrierCode ?? '',
  flightNumber: segment.flightNumber ?? '',
  originAirport: segment.originAirport ?? '',
  destinationAirport: segment.destinationAirport ?? '',
  departureLocalTime: segment.departureLocalTime ?? '',
  arrivalLocalTime: segment.arrivalLocalTime ?? '',
  durationMinutes: segment.durationMinutes ?? 0,
})

const normalizeLeg = (leg: ApiSearchResultLeg): SearchResultLeg => ({
  originAirport: leg.originAirport ?? '',
  destinationAirport: leg.destinationAirport ?? '',
  departureLocalTime: leg.departureLocalTime ?? '',
  arrivalLocalTime: leg.arrivalLocalTime ?? '',
  durationMinutes: leg.durationMinutes ?? 0,
  segments: (leg.segments ?? []).map(normalizeSegment),
})

const normalizePriceOption = (option: ApiSearchResultPriceOption): SearchResultPriceOption => ({
  id: option.id ?? '',
  provider: option.provider ?? '',
  totalPrice: {
    amount: option.totalPrice?.amount ?? 0,
    currency: option.totalPrice?.currency ?? '',
  },
  deepLink: option.deepLink ?? '',
})

const normalizeResult = (result: ApiSearchResult): SearchResult => ({
  id: result.id ?? '',
  isRoundTrip: result.isRoundTrip ?? false,
  legs: (result.legs ?? []).map(normalizeLeg),
  totalDurationMinutes: result.totalDurationMinutes ?? 0,
  priceOptions: (result.priceOptions ?? []).map(normalizePriceOption),
})

const normalizeMetadata = (metadata: ApiSearchMetadata | undefined): SearchMetadata => ({
  searchCombinationCount: metadata?.searchCombinationCount ?? 0,
  providerResultCount: metadata?.providerResultCount ?? 0,
  returnedResultCount: metadata?.returnedResultCount ?? 0,
  returnedDirectFlightCount: metadata?.returnedDirectFlightCount ?? 0,
  returnedOneStopFlightCount: metadata?.returnedOneStopFlightCount ?? 0,
  returnedTwoPlusStopFlightCount: metadata?.returnedTwoPlusStopFlightCount ?? 0,
})

const normalizeSearchResponse = (response: ApiSearchResponse): SearchResponse => ({
  results: (response.results ?? []).map(normalizeResult),
  metadata: normalizeMetadata(response.metadata),
})

const normalizeSearchSessionResponse = (session: ApiSearchSessionResponse): SearchSessionResponse => {
  return {
    searchId: session.searchId ?? '',
    status: session.status ?? 'running',
    totalCombinations: session.totalCombinations ?? 0,
    completedCombinations: session.completedCombinations ?? 0,
    failedCombinations: session.failedCombinations ?? 0,
    response: normalizeSearchResponse(session.response ?? {}),
    errorMessage: session.errorMessage ?? null,
  }
}

export const fetchAirportSuggestions = async (query: string) => {
  const res = await fetch(`${apiBaseUrl}/api/v1/airports?query=${encodeURIComponent(query)}`)
  if (!res.ok) {
    throw new Error(`Airport lookup failed with HTTP ${res.status}`)
  }

  const lookup = (await res.json()) as ApiAirportLookupResponse
  return (lookup.airports ?? []).map(normalizeAirportOption)
}

export const searchFlightsRequest = async (request: SearchRequest) => {
  const res = await fetch(`${apiBaseUrl}/api/v1/search`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request satisfies ApiSearchRequest),
  })

  if (!res.ok) {
    const message = await res.text()
    throw new Error(message || `HTTP ${res.status}`)
  }

  return normalizeSearchSessionResponse((await res.json()) as ApiSearchSessionResponse)
}

export const getSearchSession = async (searchId: string) => {
  const res = await fetch(`${apiBaseUrl}/api/v1/search/${encodeURIComponent(searchId)}`)

  if (!res.ok) {
    const message = await res.text()
    throw new Error(message || `HTTP ${res.status}`)
  }

  return normalizeSearchSessionResponse((await res.json()) as ApiSearchSessionResponse)
}
