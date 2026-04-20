import type { components } from '../../api/generated'
import type {
  AirportOption,
  SearchFilterOptionCount,
  SearchFiltersMetadata,
  SearchMetadata,
  SearchPagination,
  SearchRequest,
  SearchResponse,
  SearchResultsQuery,
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
type ApiSearchResultLegWithId = ApiSearchResultLeg & {
  id?: string | null
}
type ApiSearchResultPriceOption = components['schemas']['SearchResultPriceOption']
type ApiSearchResultBookingLink = {
  label?: string | null
  url?: string | null
  price?: {
    amount?: number | null
    currency?: string | null
  } | null
}
type ApiSearchResultPriceOptionWithLinks = ApiSearchResultPriceOption & {
  bookingLinks?: ApiSearchResultBookingLink[] | null
}
type ApiSearchResultSegment = components['schemas']['SearchResultSegment']
type ApiSearchSessionResponse = components['schemas']['SearchSessionResponse']
type ApiSearchFilterOptionCount = {
  value?: string | null
  count?: number | null
}
type ApiSearchRangeMetadata = {
  min?: number | null
  max?: number | null
}
type ApiSearchStopFilterMetadata = {
  direct?: number | null
  oneStop?: number | null
  twoPlusStop?: number | null
}
type ApiSearchFiltersMetadata = {
  providers?: ApiSearchFilterOptionCount[] | null
  airlines?: ApiSearchFilterOptionCount[] | null
  departureAirports?: ApiSearchFilterOptionCount[] | null
  arrivalAirports?: ApiSearchFilterOptionCount[] | null
  durationMinutes?: ApiSearchRangeMetadata
  departureTimeMinutes?: ApiSearchRangeMetadata
  arrivalTimeMinutes?: ApiSearchRangeMetadata
  returnDepartureTimeMinutes?: ApiSearchRangeMetadata
  returnArrivalTimeMinutes?: ApiSearchRangeMetadata
  stops?: ApiSearchStopFilterMetadata
}
type ApiSearchPagination = {
  page?: number | null
  pageSize?: number | null
  totalResults?: number | null
  totalPages?: number | null
}
type ApiSearchResponseWithFilters = ApiSearchResponse & {
  filters?: ApiSearchFiltersMetadata
  pagination?: ApiSearchPagination
}

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

const normalizeLeg = (leg: ApiSearchResultLegWithId): SearchResultLeg => ({
  id: leg.id ?? '',
  originAirport: leg.originAirport ?? '',
  destinationAirport: leg.destinationAirport ?? '',
  departureLocalTime: leg.departureLocalTime ?? '',
  arrivalLocalTime: leg.arrivalLocalTime ?? '',
  durationMinutes: leg.durationMinutes ?? 0,
  segments: (leg.segments ?? []).map(normalizeSegment),
})

const normalizePriceOption = (option: ApiSearchResultPriceOptionWithLinks): SearchResultPriceOption => ({
  id: option.id ?? '',
  provider: option.provider ?? '',
  totalPrice: {
    amount: option.totalPrice?.amount ?? 0,
    currency: option.totalPrice?.currency ?? '',
  },
  bookingLinks: (() => {
    const explicitLinks = ((option.bookingLinks as ApiSearchResultBookingLink[] | null | undefined) ?? [])
      .map((link) => ({
        label: link.label ?? '',
        url: link.url ?? '',
        price: link.price
          ? {
              amount: link.price.amount ?? 0,
              currency: link.price.currency ?? '',
            }
          : null,
      }))
      .filter((link) => link.url)

    if (explicitLinks.length > 0) {
      return explicitLinks
    }

    return option.deepLink
      ? [{ label: 'View fare', url: option.deepLink }]
      : []
  })(),
})

const normalizeResult = (result: ApiSearchResult): SearchResult => ({
  id: result.id ?? '',
  isRoundTrip: result.isRoundTrip ?? false,
  legs: ((result.legs as ApiSearchResultLegWithId[] | null | undefined) ?? []).map(normalizeLeg),
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

const normalizeFilterOptionCount = (option: ApiSearchFilterOptionCount): SearchFilterOptionCount => ({
  value: option.value ?? '',
  count: option.count ?? 0,
})

const normalizeRange = (range: ApiSearchRangeMetadata | undefined) => ({
  min: range?.min ?? 0,
  max: range?.max ?? 0,
})

const normalizeFilters = (filters: ApiSearchFiltersMetadata | undefined): SearchFiltersMetadata => ({
  providers: (filters?.providers ?? []).map(normalizeFilterOptionCount),
  airlines: (filters?.airlines ?? []).map(normalizeFilterOptionCount),
  departureAirports: (filters?.departureAirports ?? []).map(normalizeFilterOptionCount),
  arrivalAirports: (filters?.arrivalAirports ?? []).map(normalizeFilterOptionCount),
  durationMinutes: normalizeRange(filters?.durationMinutes),
  departureTimeMinutes: normalizeRange(filters?.departureTimeMinutes),
  arrivalTimeMinutes: normalizeRange(filters?.arrivalTimeMinutes),
  returnDepartureTimeMinutes: normalizeRange(filters?.returnDepartureTimeMinutes),
  returnArrivalTimeMinutes: normalizeRange(filters?.returnArrivalTimeMinutes),
  stops: {
    direct: filters?.stops?.direct ?? 0,
    oneStop: filters?.stops?.oneStop ?? 0,
    twoPlusStop: filters?.stops?.twoPlusStop ?? 0,
  },
})

const normalizePagination = (pagination: ApiSearchPagination | undefined, resultCount: number): SearchPagination => ({
  page: pagination?.page ?? 1,
  pageSize: pagination?.pageSize ?? resultCount,
  totalResults: pagination?.totalResults ?? resultCount,
  totalPages: pagination?.totalPages ?? (resultCount > 0 ? 1 : 0),
})

const normalizeSearchResponse = (response: ApiSearchResponseWithFilters): SearchResponse => ({
  results: (response.results ?? []).map(normalizeResult),
  metadata: normalizeMetadata(response.metadata),
  filters: normalizeFilters(response.filters),
  pagination: normalizePagination(response.pagination, (response.results ?? []).length),
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

const buildResultsQueryParams = (query: SearchResultsQuery) => {
  const params = new URLSearchParams()

  if (query.direct !== undefined) {
    params.set('direct', query.direct ? 'true' : 'false')
  }

  if (query.oneStop !== undefined) {
    params.set('oneStop', query.oneStop ? 'true' : 'false')
  }

  if (query.twoPlusStop !== undefined) {
    params.set('twoPlusStop', query.twoPlusStop ? 'true' : 'false')
  }

  const setListParam = (key: string, values?: string[]) => {
    if (!values || values.length === 0) {
      return
    }

    params.set(key, values.join(','))
  }

  setListParam('providers', query.providers)
  setListParam('airlines', query.airlines)
  setListParam('departureAirports', query.departureAirports)
  setListParam('arrivalAirports', query.arrivalAirports)

  if (query.maxDuration !== undefined) {
    params.set('maxDuration', String(query.maxDuration))
  }

  if (query.departureTime) {
    params.set('departureTime', `${query.departureTime[0]}-${query.departureTime[1]}`)
  }

  if (query.arrivalTime) {
    params.set('arrivalTime', `${query.arrivalTime[0]}-${query.arrivalTime[1]}`)
  }

  if (query.returnDepartureTime) {
    params.set('returnDepartureTime', `${query.returnDepartureTime[0]}-${query.returnDepartureTime[1]}`)
  }

  if (query.returnArrivalTime) {
    params.set('returnArrivalTime', `${query.returnArrivalTime[0]}-${query.returnArrivalTime[1]}`)
  }

  if (query.outboundLegId) {
    params.set('outboundLegId', query.outboundLegId)
  }

  if (query.returnLegId) {
    params.set('returnLegId', query.returnLegId)
  }

  if (query.page !== undefined) {
    params.set('page', String(query.page))
  }

  if (query.pageSize !== undefined) {
    params.set('pageSize', String(query.pageSize))
  }

  return params
}

export const getSearchSession = async (searchId: string, query: SearchResultsQuery = {}) => {
  const queryString = buildResultsQueryParams(query).toString()
  const url = queryString
    ? `${apiBaseUrl}/api/v1/search/${encodeURIComponent(searchId)}?${queryString}`
    : `${apiBaseUrl}/api/v1/search/${encodeURIComponent(searchId)}`

  const res = await fetch(url)

  if (!res.ok) {
    const message = await res.text()
    throw new Error(message || `HTTP ${res.status}`)
  }

  return normalizeSearchSessionResponse((await res.json()) as ApiSearchSessionResponse)
}
