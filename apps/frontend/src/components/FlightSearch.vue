<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter, type LocationQueryValue } from 'vue-router'
import FlightSearchBar from './flight-search/FlightSearchBar.vue'
import SearchFilters from './flight-search/SearchFilters.vue'
import SearchResultCard from './flight-search/SearchResultCard.vue'
import { fetchAirportSuggestions, getSearchSession, searchFlightsRequest } from '../features/flight-search/api'
import {
  cabinOptions,
  type AirportOption,
  type SearchRequest,
  type SearchResultsQuery,
  type SearchResponse,
  type SearchSessionResponse,
} from '../features/flight-search/types'

const MAX_DEPARTURE_RANGE_DAYS = 10
const DEFAULT_PAGE_SIZE = 100

const originInput = ref('')
const destinationInput = ref('')
const originAirports = ref<AirportOption[]>([
  { code: 'DUB', name: 'Dublin', displayLabel: 'Dublin (DUB)' },
])
const destinationAirports = ref<AirportOption[]>([
  { code: 'AMS', name: 'Amsterdam Schiphol', displayLabel: 'Amsterdam Schiphol (AMS)' },
])

const tripType = ref<'oneWay' | 'return'>('oneWay')
const departureDateFrom = ref('2026-05-15')
const departureDateTo = ref('2026-05-17')
const selectedDepartureDates = ref<string[]>(['2026-05-15', '2026-05-16', '2026-05-17'])
const returnDateFrom = ref<string | null>(null)
const returnDateTo = ref<string | null>(null)
const selectedReturnDates = ref<string[]>([])
const adults = ref(1)
const cabinClass = ref('economy')

const originSuggestions = ref<AirportOption[]>([])
const destinationSuggestions = ref<AirportOption[]>([])

const loading = ref(false)
const error = ref<string | null>(null)
const response = ref<SearchResponse | null>(null)
const searchSession = ref<SearchSessionResponse | null>(null)
const loadedResults = ref<SearchResponse['results']>([])
const expandedResultIds = ref<string[]>([])
const isSearchCollapsed = ref(false)

const includeDirectFlights = ref(true)
const includeOneStopFlights = ref(false)
const includeTwoPlusStopFlights = ref(false)
const selectedProviders = ref<string[]>([])
const selectedAirlines = ref<string[]>([])
const selectedDepartureAirports = ref<string[]>([])
const selectedArrivalAirports = ref<string[]>([])
const maxDurationMinutes = ref(0)
const departureTimeRange = ref<[number, number]>([0, 1439])
const arrivalTimeRange = ref<[number, number]>([0, 1439])
const currentPage = ref(1)
const isLoadingMore = ref(false)
const loadMoreSentinel = ref<HTMLElement | null>(null)

let originRequestId = 0
let destinationRequestId = 0
let pollingTimer: number | null = null
let filterRefreshTimer: number | null = null
let loadMoreObserver: IntersectionObserver | null = null
let hasMounted = false
let hasHydratedFiltersFromUrl = false
let isSyncingRoute = false
let lastExecutedSearchKey: string | null = null

const route = useRoute()
const router = useRouter()

const getQueryString = (value: LocationQueryValue | LocationQueryValue[] | undefined) =>
  Array.isArray(value) ? value[0] ?? null : value ?? null

const buildAirportOption = (code: string): AirportOption => ({
  code,
  name: null,
  displayLabel: code,
})

const parseStringListParam = (value: string | null) =>
  (value ?? '')
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)

const parseCodeListParam = (value: string | null) =>
  parseStringListParam(value).map((item) => item.toUpperCase())

const parseDateListParam = (value: string | null) =>
  (value ?? '')
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)
    .sort((left, right) => left.localeCompare(right))

const parseNumberParam = (value: string | null, fallback: number) => {
  if (value === null || value.trim() === '') {
    return fallback
  }

  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

const parseBooleanParam = (value: string | null, fallback: boolean) => {
  if (value === '1' || value === 'true') {
    return true
  }

  if (value === '0' || value === 'false') {
    return false
  }

  return fallback
}

const parseRangeParam = (value: string | null, fallback: [number, number]): [number, number] => {
  if (!value) {
    return fallback
  }

  const [startRaw, endRaw] = value.split('-', 2)
  const start = Number(startRaw)
  const end = Number(endRaw)

  if (!Number.isFinite(start) || !Number.isFinite(end)) {
    return fallback
  }

  return [Math.min(start, end), Math.max(start, end)]
}

const hasActiveFilterQuery = (params: Record<string, LocationQueryValue | LocationQueryValue[] | undefined>) =>
  [
    'direct',
    'oneStop',
    'twoPlusStop',
    'providers',
    'airlines',
    'departureAirports',
    'arrivalAirports',
    'maxDuration',
    'departureTime',
    'arrivalTime',
  ].some((key) => params[key] !== undefined)

const buildSearchRequestKey = (
  origins: string[],
  destinations: string[],
  dates: string[],
  tripTypeValue: 'oneWay' | 'return',
  returnDateFromValue: string | null,
  returnDateToValue: string | null,
  adultsValue: number,
  cabinClassValue: string,
) => JSON.stringify({
  origins: [...origins].sort((left, right) => left.localeCompare(right)),
  destinations: [...destinations].sort((left, right) => left.localeCompare(right)),
  dates: [...dates].sort((left, right) => left.localeCompare(right)),
  tripType: tripTypeValue,
  returnDateFrom: returnDateFromValue,
  returnDateTo: returnDateToValue,
  adults: adultsValue,
  cabinClass: cabinClassValue,
})

const getCurrentSearchRequestKey = () => {
  const origins = uniqueAirportCodes(originAirports.value)
  const destinations = uniqueAirportCodes(destinationAirports.value)
  const dates = [...selectedDepartureDates.value].sort((left, right) => left.localeCompare(right))

  if (origins.length === 0 || destinations.length === 0 || dates.length === 0) {
    return null
  }

  return buildSearchRequestKey(
    origins,
    destinations,
    dates,
    tripType.value,
    returnDateFrom.value,
    returnDateTo.value,
    adults.value,
    cabinClass.value,
  )
}

const getSearchRequestKeyFromQuery = (params: Record<string, LocationQueryValue | LocationQueryValue[] | undefined>) => {
  const origins = parseCodeListParam(getQueryString(params.origins))
  const destinations = parseCodeListParam(getQueryString(params.destinations))
  const dates = parseDateListParam(getQueryString(params.dates))

  if (origins.length === 0 || destinations.length === 0 || dates.length === 0) {
    return null
  }

  return buildSearchRequestKey(
    origins,
    destinations,
    dates,
    getQueryString(params.tripType) === 'return' ? 'return' : 'oneWay',
    getQueryString(params.returnDateFrom),
    getQueryString(params.returnDateTo),
    parseNumberParam(getQueryString(params.adults), 1),
    getQueryString(params.cabinClass)?.trim() || 'economy',
  )
}

const applyUrlState = () => {
  const params = route.query

  const originCodes = parseCodeListParam(getQueryString(params.origins))
  if (originCodes.length > 0) {
    originAirports.value = originCodes.map(buildAirportOption)
  }

  const destinationCodes = parseCodeListParam(getQueryString(params.destinations))
  if (destinationCodes.length > 0) {
    destinationAirports.value = destinationCodes.map(buildAirportOption)
  }

  const selectedDates = parseDateListParam(getQueryString(params.dates))
  if (selectedDates.length > 0) {
    selectedDepartureDates.value = selectedDates
    departureDateFrom.value = selectedDates[0]
    departureDateTo.value = selectedDates[selectedDates.length - 1]
  }

  tripType.value = getQueryString(params.tripType) === 'return' ? 'return' : 'oneWay'
  returnDateFrom.value = getQueryString(params.returnDateFrom)
  returnDateTo.value = getQueryString(params.returnDateTo)
  selectedReturnDates.value = buildReturnDatesFromBounds(returnDateFrom.value, returnDateTo.value)

  adults.value = parseNumberParam(getQueryString(params.adults), adults.value)

  const cabinClassParam = getQueryString(params.cabinClass)?.trim()
  if (cabinClassParam) {
    cabinClass.value = cabinClassParam
  }

  includeDirectFlights.value = parseBooleanParam(getQueryString(params.direct), includeDirectFlights.value)
  includeOneStopFlights.value = parseBooleanParam(getQueryString(params.oneStop), includeOneStopFlights.value)
  includeTwoPlusStopFlights.value = parseBooleanParam(getQueryString(params.twoPlusStop), includeTwoPlusStopFlights.value)
  selectedProviders.value = parseStringListParam(getQueryString(params.providers))
  selectedAirlines.value = parseStringListParam(getQueryString(params.airlines))
  selectedDepartureAirports.value = parseCodeListParam(getQueryString(params.departureAirports))
  selectedArrivalAirports.value = parseCodeListParam(getQueryString(params.arrivalAirports))
  maxDurationMinutes.value = parseNumberParam(getQueryString(params.maxDuration), maxDurationMinutes.value)
  departureTimeRange.value = parseRangeParam(getQueryString(params.departureTime), departureTimeRange.value)
  arrivalTimeRange.value = parseRangeParam(getQueryString(params.arrivalTime), arrivalTimeRange.value)
  hasHydratedFiltersFromUrl = hasActiveFilterQuery(params)
}

const setListParam = (params: Record<string, string>, key: string, values: string[]) => {
  const cleanedValues = values.map((value) => value.trim()).filter(Boolean)
  if (cleanedValues.length === 0) {
    delete params[key]
    return
  }

  params[key] = cleanedValues.join(',')
}

const setBooleanParam = (params: Record<string, string>, key: string, value: boolean, fallback: boolean) => {
  if (value === fallback) {
    delete params[key]
    return
  }

  params[key] = value ? '1' : '0'
}

const setNumberParam = (params: Record<string, string>, key: string, value: number, fallback: number) => {
  if (value === fallback) {
    delete params[key]
    return
  }

  params[key] = String(value)
}

const setRangeParam = (params: Record<string, string>, key: string, value: [number, number], fallback: [number, number]) => {
  if (value[0] === fallback[0] && value[1] === fallback[1]) {
    delete params[key]
    return
  }

  params[key] = `${value[0]}-${value[1]}`
}

const getExplicitSelection = (selectedValues: string[], availableValues: string[]) => {
  const cleanedSelectedValues = selectedValues.map((value) => value.trim()).filter(Boolean)
  const cleanedAvailableValues = availableValues.map((value) => value.trim()).filter(Boolean)

  if (
    cleanedSelectedValues.length === 0 ||
    (cleanedAvailableValues.length > 0 &&
      cleanedSelectedValues.length === cleanedAvailableValues.length &&
      cleanedAvailableValues.every((value) => cleanedSelectedValues.includes(value)))
  ) {
    return []
  }

  return cleanedSelectedValues
}

const updateRouteState = async () => {
  if (!hasMounted || isSyncingRoute) {
    return
  }

  const query: Record<string, string> = {}
  setListParam(query, 'origins', originAirports.value.map((airport) => airport.code))
  setListParam(query, 'destinations', destinationAirports.value.map((airport) => airport.code))
  setListParam(query, 'dates', selectedDepartureDates.value)
  if (tripType.value === 'return') {
    query.tripType = 'return'
    if (returnDateFrom.value) {
      query.returnDateFrom = returnDateFrom.value
    }
    if (returnDateTo.value) {
      query.returnDateTo = returnDateTo.value
    }
  }
  query.adults = String(adults.value)

  if (cabinClass.value !== 'economy') {
    query.cabinClass = cabinClass.value
  }

  setBooleanParam(query, 'direct', includeDirectFlights.value, true)
  setBooleanParam(query, 'oneStop', includeOneStopFlights.value, false)
  setBooleanParam(query, 'twoPlusStop', includeTwoPlusStopFlights.value, false)
  setListParam(query, 'providers', getExplicitSelection(selectedProviders.value, providerFilters.value))
  setListParam(query, 'airlines', getExplicitSelection(selectedAirlines.value, airlineFilters.value))
  setListParam(query, 'departureAirports', getExplicitSelection(selectedDepartureAirports.value, departureAirportFilters.value))
  setListParam(query, 'arrivalAirports', getExplicitSelection(selectedArrivalAirports.value, arrivalAirportFilters.value))
  setNumberParam(query, 'maxDuration', maxDurationMinutes.value, response.value ? availableMaxDurationMinutes.value : 0)
  setRangeParam(query, 'departureTime', departureTimeRange.value, [0, 1439])
  setRangeParam(query, 'arrivalTime', arrivalTimeRange.value, [0, 1439])

  isSyncingRoute = true
  try {
    await router.replace({ query })
  } finally {
    isSyncingRoute = false
  }
}

const syncSearchFromRoute = () => {
  const routeSearchKey = getSearchRequestKeyFromQuery(route.query)
  if (!routeSearchKey || routeSearchKey === lastExecutedSearchKey) {
    return
  }

  void searchFlights()
}

const providerFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return response.value.filters.providers.map((option: { value: string }) => option.value)
})

const airlineFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return response.value.filters.airlines.map((option: { value: string }) => option.value)
})

const departureAirportFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return response.value.filters.departureAirports.map((option: { value: string }) => option.value)
})

const arrivalAirportFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return response.value.filters.arrivalAirports.map((option: { value: string }) => option.value)
})

const availableMaxDurationMinutes = computed(() => {
  if (!response.value) {
    return 0
  }

  return response.value.filters.durationMinutes.max
})

const filteredResults = computed(() => loadedResults.value)
const totalPages = computed(() => response.value?.pagination.totalPages ?? 0)
const hasMoreResults = computed(() => response.value !== null && currentPage.value < totalPages.value)
const paginationSummary = computed(() => {
  if (!response.value || response.value.pagination.totalResults === 0 || filteredResults.value.length === 0) {
    return 'No results'
  }

  const totalResults = response.value.pagination.totalResults
  const end = filteredResults.value.length

  return `Showing ${end} of ${totalResults}`
})

const loadedStopCounts = computed(() => {
  const counts = {
    direct: 0,
    oneStop: 0,
    twoPlusStop: 0,
  }

  for (const result of filteredResults.value) {
    const stops = result.legs.reduce((sum, leg) => sum + Math.max(leg.segments.length - 1, 0), 0)

    if (stops <= 0) {
      counts.direct += 1
    } else if (stops === 1) {
      counts.oneStop += 1
    } else {
      counts.twoPlusStop += 1
    }
  }

  return counts
})

const searchSummary = computed(() => {
  if (!response.value) {
    return 'Search across multiple airports and compare grouped fares.'
  }

  return `${response.value.pagination.totalResults} flights after filters`
})

const progressPercentage = computed(() => {
  if (!searchSession.value || searchSession.value.totalCombinations === 0) {
    return 0
  }

  return Math.round((searchSession.value.completedCombinations / searchSession.value.totalCombinations) * 100)
})

const isPolling = computed(() =>
  searchSession.value?.status === 'running',
)

const compactSearchSummary = computed(() => {
  const origins = originAirports.value.map((airport) => airport.code).join(', ')
  const destinations = destinationAirports.value.map((airport) => airport.code).join(', ')
  const dateSummary = selectedDepartureDates.value.join(', ')
  const returnSummary = tripType.value === 'return' && returnDateFrom.value && returnDateTo.value
    ? ` returning ${returnDateFrom.value}${returnDateTo.value !== returnDateFrom.value ? ` to ${returnDateTo.value}` : ''}`
    : ''
  return `${origins} to ${destinations} on ${dateSummary}${returnSummary}`
})

const uniqueAirportCodes = (airports: AirportOption[]) =>
  [...new Set(airports.map((airport) => airport.code.trim().toUpperCase()).filter(Boolean))]

const pageTitle = computed(() => {
  const origins = uniqueAirportCodes(originAirports.value).join(', ')
  const destinations = uniqueAirportCodes(destinationAirports.value).join(', ')
  const routeSummary = origins && destinations ? `${origins} to ${destinations}` : ''

  if (isPolling.value && routeSummary) {
    return `Aveon · Searching ${routeSummary}`
  }

  if (response.value && routeSummary) {
    return `Aveon · ${response.value.pagination.totalResults} flights from ${routeSummary}`
  }

  return 'Aveon'
})

const searchCombinationCount = computed(() => {
  const origins = uniqueAirportCodes(originAirports.value)
  const destinations = uniqueAirportCodes(destinationAirports.value)
  const departureDateCount = selectedDepartureDates.value.length

  if (origins.length === 0 || destinations.length === 0 || departureDateCount <= 0) {
    return 0
  }

  const routeCombinationCount = origins.reduce((count, origin) => (
    count + destinations.filter((destination) => destination !== origin).length
  ), 0)

  const returnDateCount = tripType.value === 'return'
    ? buildReturnDates().length
    : 1

  return routeCombinationCount * departureDateCount * returnDateCount
})

const addDays = (dateString: string, days: number) => {
  const date = new Date(`${dateString}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

const buildDateRange = (start: string | null, end: string | null) => {
  if (!start || !end) {
    return []
  }

  const first = start <= end ? start : end
  const last = start <= end ? end : start
  const dates: string[] = []

  for (let dateValue = first; dateValue <= last; dateValue = addDays(dateValue, 1)) {
    dates.push(dateValue)
  }

  return dates
}

const buildReturnDatesFromBounds = (start: string | null, end: string | null) =>
  buildDateRange(start, end)

const buildReturnDates = () =>
  tripType.value === 'return'
    ? buildReturnDatesFromBounds(returnDateFrom.value, returnDateTo.value)
    : []

const isSelected = (items: AirportOption[], code: string) =>
  items.some((item) => item.code === code)

const removeAirport = (
  items: typeof originAirports | typeof destinationAirports,
  code: string,
) => {
  items.value = items.value.filter((item) => item.code !== code)
}

const addAirport = (
  items: typeof originAirports | typeof destinationAirports,
  input: typeof originInput | typeof destinationInput,
  suggestions: typeof originSuggestions | typeof destinationSuggestions,
  airport: AirportOption,
) => {
  if (isSelected(items.value, airport.code)) {
    input.value = ''
    suggestions.value = []
    return
  }

  items.value = [...items.value, airport]
  input.value = ''
  suggestions.value = []
}

const tryAddFromInput = (
  items: typeof originAirports | typeof destinationAirports,
  input: typeof originInput | typeof destinationInput,
  suggestions: typeof originSuggestions | typeof destinationSuggestions,
) => {
  const trimmed = input.value.trim()
  if (!trimmed) {
    return
  }

  const match = suggestions.value.find((option) =>
    option.code.toLowerCase() === trimmed.toLowerCase() ||
    option.displayLabel.toLowerCase() === trimmed.toLowerCase(),
  )

  if (match) {
    addAirport(items, input, suggestions, match)
  }
}

const updateSuggestions = async (
  query: string,
  items: typeof originAirports | typeof destinationAirports,
  suggestions: typeof originSuggestions | typeof destinationSuggestions,
  requestId: number,
  getLatestRequestId: () => number,
) => {
  const trimmed = query.trim()
  if (trimmed.length < 2) {
    suggestions.value = []
    return
  }

  try {
    const lookup = await fetchAirportSuggestions(trimmed)
    if (requestId !== getLatestRequestId()) {
      return
    }

    suggestions.value = lookup.filter((airport) => !isSelected(items.value, airport.code))
  } catch {
    if (requestId === getLatestRequestId()) {
      suggestions.value = []
    }
  }
}

const arraysEqual = (left: string[], right: string[]) =>
  left.length === right.length && left.every((value, index) => value === right[index])

const syncSelectedFiltersToAvailable = (
  selectedItems: typeof selectedProviders | typeof selectedAirlines | typeof selectedDepartureAirports | typeof selectedArrivalAirports,
  availableItems: string[],
  previousAvailableItems: string[],
  shouldReset: boolean,
) => {
  const hadAllAvailableSelected =
    previousAvailableItems.length === 0
      ? selectedItems.value.length === 0
      : previousAvailableItems.every((item) => selectedItems.value.includes(item))

  if (shouldReset || hadAllAvailableSelected) {
    if (!arraysEqual(selectedItems.value, availableItems)) {
      selectedItems.value = [...availableItems]
    }
    return
  }

  const nextSelectedItems = selectedItems.value.filter((item) => availableItems.includes(item))
  if (!arraysEqual(selectedItems.value, nextSelectedItems)) {
    selectedItems.value = nextSelectedItems
  }
}

const syncMaxDurationToAvailable = (shouldReset: boolean) => {
  if (shouldReset) {
    if (maxDurationMinutes.value !== availableMaxDurationMinutes.value) {
      maxDurationMinutes.value = availableMaxDurationMinutes.value
    }
    return
  }

  const nextMaxDuration = Math.min(maxDurationMinutes.value, availableMaxDurationMinutes.value)
  if (maxDurationMinutes.value !== nextMaxDuration) {
    maxDurationMinutes.value = nextMaxDuration
  }
}

const buildSearchResultsQuery = (): SearchResultsQuery => {
  const query: SearchResultsQuery = {
    direct: includeDirectFlights.value,
    oneStop: includeOneStopFlights.value,
    twoPlusStop: includeTwoPlusStopFlights.value,
    pageSize: DEFAULT_PAGE_SIZE,
  }

  const explicitProviders = getExplicitSelection(selectedProviders.value, providerFilters.value)
  if (explicitProviders.length > 0) {
    query.providers = explicitProviders
  }

  const explicitAirlines = getExplicitSelection(selectedAirlines.value, airlineFilters.value)
  if (explicitAirlines.length > 0) {
    query.airlines = explicitAirlines
  }

  const explicitDepartureAirports = getExplicitSelection(selectedDepartureAirports.value, departureAirportFilters.value)
  if (explicitDepartureAirports.length > 0) {
    query.departureAirports = explicitDepartureAirports
  }

  const explicitArrivalAirports = getExplicitSelection(selectedArrivalAirports.value, arrivalAirportFilters.value)
  if (explicitArrivalAirports.length > 0) {
    query.arrivalAirports = explicitArrivalAirports
  }

  if (response.value && maxDurationMinutes.value > 0 && maxDurationMinutes.value < availableMaxDurationMinutes.value) {
    query.maxDuration = maxDurationMinutes.value
  }

  if (departureTimeRange.value[0] !== 0 || departureTimeRange.value[1] !== 1439) {
    query.departureTime = [...departureTimeRange.value] as [number, number]
  }

  if (arrivalTimeRange.value[0] !== 0 || arrivalTimeRange.value[1] !== 1439) {
    query.arrivalTime = [...arrivalTimeRange.value] as [number, number]
  }

  return query
}

watch(originInput, (value) => {
  originRequestId += 1
  void updateSuggestions(value, originAirports, originSuggestions, originRequestId, () => originRequestId)
})

watch(destinationInput, (value) => {
  destinationRequestId += 1
  void updateSuggestions(value, destinationAirports, destinationSuggestions, destinationRequestId, () => destinationRequestId)
})

watch(departureDateFrom, (value) => {
  if (departureDateTo.value < value) {
    departureDateTo.value = value
    return
  }

  const maxAllowedEnd = addDays(value, MAX_DEPARTURE_RANGE_DAYS - 1)
  if (departureDateTo.value > maxAllowedEnd) {
    departureDateTo.value = maxAllowedEnd
  }
})

watch(departureDateTo, (value) => {
  if (departureDateFrom.value > value) {
    departureDateFrom.value = value
    return
  }

  const minAllowedStart = addDays(value, -(MAX_DEPARTURE_RANGE_DAYS - 1))
  if (departureDateFrom.value < minAllowedStart) {
    departureDateFrom.value = minAllowedStart
  }
})

watch(tripType, (value) => {
  if (value === 'oneWay') {
    returnDateFrom.value = null
    returnDateTo.value = null
    selectedReturnDates.value = []
    return
  }

  if (!returnDateFrom.value) {
    returnDateFrom.value = departureDateFrom.value
  }

  if (!returnDateTo.value) {
    returnDateTo.value = returnDateFrom.value
  }

  selectedReturnDates.value = buildReturnDates()
})

watch(returnDateFrom, (value) => {
  if (!value) {
    return
  }

  if (value < departureDateFrom.value) {
    returnDateFrom.value = departureDateFrom.value
    return
  }

  if (returnDateTo.value && returnDateTo.value < value) {
    returnDateTo.value = value
  }

  selectedReturnDates.value = buildReturnDates()
})

watch(returnDateTo, (value) => {
  if (!value || !returnDateFrom.value) {
    return
  }

  if (value < returnDateFrom.value) {
    returnDateTo.value = returnDateFrom.value
    return
  }

  selectedReturnDates.value = buildReturnDates()
})

watch(
  response,
  (nextResponse, previousResponse) => {
    const shouldResetFilters = !previousResponse && Boolean(nextResponse) && !hasHydratedFiltersFromUrl
    const previousProviderFilters = previousResponse?.filters.providers.map((option: { value: string }) => option.value) ?? []
    const previousAirlineFilters = previousResponse?.filters.airlines.map((option: { value: string }) => option.value) ?? []
    const previousDepartureAirportFilters = previousResponse?.filters.departureAirports.map((option: { value: string }) => option.value) ?? []
    const previousArrivalAirportFilters = previousResponse?.filters.arrivalAirports.map((option: { value: string }) => option.value) ?? []

    syncSelectedFiltersToAvailable(selectedProviders, providerFilters.value, previousProviderFilters, shouldResetFilters)
    syncSelectedFiltersToAvailable(selectedAirlines, airlineFilters.value, previousAirlineFilters, shouldResetFilters)
    syncSelectedFiltersToAvailable(selectedDepartureAirports, departureAirportFilters.value, previousDepartureAirportFilters, shouldResetFilters)
    syncSelectedFiltersToAvailable(selectedArrivalAirports, arrivalAirportFilters.value, previousArrivalAirportFilters, shouldResetFilters)
    syncMaxDurationToAvailable(shouldResetFilters)
    hasHydratedFiltersFromUrl = false
  },
  { immediate: true },
)

watch(
  [
    includeDirectFlights,
    includeOneStopFlights,
    includeTwoPlusStopFlights,
    selectedProviders,
    selectedAirlines,
    selectedDepartureAirports,
    selectedArrivalAirports,
    maxDurationMinutes,
    departureTimeRange,
    arrivalTimeRange,
  ],
  () => {
    if (currentPage.value !== 1) {
      currentPage.value = 1
    }
  },
  { deep: true },
)

watch(
  [
    originAirports,
    destinationAirports,
    tripType,
    selectedDepartureDates,
    returnDateFrom,
    returnDateTo,
    selectedReturnDates,
    adults,
    cabinClass,
    includeDirectFlights,
    includeOneStopFlights,
    includeTwoPlusStopFlights,
    selectedProviders,
    selectedAirlines,
    selectedDepartureAirports,
    selectedArrivalAirports,
    maxDurationMinutes,
    departureTimeRange,
    arrivalTimeRange,
    isSearchCollapsed,
  ],
  () => {
    void updateRouteState()
  },
  { deep: true },
)

const loadSearchSession = async (
  searchId: string,
  options: { page?: number; append?: boolean } = {},
) => {
  const page = options.page ?? currentPage.value
  const append = options.append ?? false
  const session = await getSearchSession(searchId, {
    ...buildSearchResultsQuery(),
    page,
  })
  searchSession.value = session
  loadedResults.value = append
    ? [...loadedResults.value, ...session.response.results]
    : [...session.response.results]
  response.value = {
    ...session.response,
    results: [...loadedResults.value],
  }
  error.value = session.errorMessage ?? null
  currentPage.value = page

  return session
}

const scheduleSearchSessionRefresh = () => {
  if (!hasMounted || !searchSession.value?.searchId) {
    return
  }

  if (filterRefreshTimer !== null) {
    window.clearTimeout(filterRefreshTimer)
  }

  filterRefreshTimer = window.setTimeout(() => {
    filterRefreshTimer = null
    void loadSearchSession(searchSession.value!.searchId, { page: 1, append: false })
  }, 200)
}

watch(
  [
    includeDirectFlights,
    includeOneStopFlights,
    includeTwoPlusStopFlights,
    selectedProviders,
    selectedAirlines,
    selectedDepartureAirports,
    selectedArrivalAirports,
    maxDurationMinutes,
    departureTimeRange,
    arrivalTimeRange,
  ],
  () => {
    scheduleSearchSessionRefresh()
  },
  { deep: true },
)

watch(
  () => route.query,
  () => {
    if (isSyncingRoute) {
      return
    }

    applyUrlState()
    syncSearchFromRoute()
  },
)

watch(
  pageTitle,
  (value) => {
    document.title = value
  },
  { immediate: true },
)

const setupLoadMoreObserver = () => {
  loadMoreObserver?.disconnect()
  loadMoreObserver = null

  if (!loadMoreSentinel.value || typeof IntersectionObserver === 'undefined') {
    return
  }

  loadMoreObserver = new IntersectionObserver(
    (entries) => {
      if (entries.some((entry) => entry.isIntersecting)) {
        void loadNextPage()
      }
    },
    {
      root: null,
      rootMargin: '0px 0px 320px 0px',
      threshold: 0,
    },
  )

  loadMoreObserver.observe(loadMoreSentinel.value)
}

onMounted(() => {
  applyUrlState()
  hasMounted = true
  void updateRouteState()
  void fetchAirportSuggestions(originAirports.value[0].code)
  void fetchAirportSuggestions(destinationAirports.value[0].code)
  syncSearchFromRoute()
  setupLoadMoreObserver()
})

watch(loadMoreSentinel, () => {
  setupLoadMoreObserver()
})

watch(hasMoreResults, () => {
  setupLoadMoreObserver()
})

onBeforeUnmount(() => {
  stopPolling()
  if (filterRefreshTimer !== null) {
    window.clearTimeout(filterRefreshTimer)
  }
  loadMoreObserver?.disconnect()
})

const stopPolling = () => {
  if (pollingTimer !== null) {
    window.clearTimeout(pollingTimer)
    pollingTimer = null
  }
}

const pollSearchSession = async (searchId: string) => {
  try {
    const session = await loadSearchSession(searchId, { page: 1, append: false })

    if (session.status === 'running') {
      pollingTimer = window.setTimeout(() => {
        void pollSearchSession(searchId)
      }, 1000)
      return
    }

    stopPolling()
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Unknown error'
    stopPolling()
  }
}

const searchFlights = async () => {
  stopPolling()
  loading.value = true
  error.value = null
  response.value = null
  searchSession.value = null
  loadedResults.value = []
  expandedResultIds.value = []

  try {
    lastExecutedSearchKey = getCurrentSearchRequestKey()

    const request: SearchRequest = {
      originAirports: originAirports.value.map((airport) => airport.code),
      destinationAirports: destinationAirports.value.map((airport) => airport.code),
      selectedDates: [...selectedDepartureDates.value],
      returnDateFrom: tripType.value === 'return' ? returnDateFrom.value : null,
      returnDateTo: tripType.value === 'return' ? returnDateTo.value : null,
      adults: adults.value,
      cabinClass: cabinClass.value,
    }

    const session = await searchFlightsRequest(request)
    searchSession.value = session
    loadedResults.value = [...session.response.results]
    response.value = {
      ...session.response,
      results: [...loadedResults.value],
    }
    isSearchCollapsed.value = true
    currentPage.value = 1
    loading.value = false

    if (session.status === 'running') {
      await pollSearchSession(session.searchId)
    } else {
      await loadSearchSession(session.searchId, { page: 1, append: false })
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Unknown error'
  } finally {
    if (!isPolling.value) {
      loading.value = false
    }
  }
}

const toggleExpanded = (resultId: string) => {
  if (expandedResultIds.value.includes(resultId)) {
    expandedResultIds.value = expandedResultIds.value.filter((id) => id !== resultId)
    return
  }

  expandedResultIds.value = [...expandedResultIds.value, resultId]
}

const isExpanded = (resultId: string) => expandedResultIds.value.includes(resultId)

const removeOriginAirport = (code: string) => removeAirport(originAirports, code)
const removeDestinationAirport = (code: string) => removeAirport(destinationAirports, code)
const addOriginAirport = (airport: AirportOption) => addAirport(originAirports, originInput, originSuggestions, airport)
const addDestinationAirport = (airport: AirportOption) => addAirport(destinationAirports, destinationInput, destinationSuggestions, airport)
const confirmOriginInput = () => tryAddFromInput(originAirports, originInput, originSuggestions)
const confirmDestinationInput = () => tryAddFromInput(destinationAirports, destinationInput, destinationSuggestions)

const loadNextPage = async () => {
  if (!searchSession.value?.searchId || isLoadingMore.value || !hasMoreResults.value || isPolling.value) {
    return
  }

  isLoadingMore.value = true
  try {
    await loadSearchSession(searchSession.value.searchId, {
      page: currentPage.value + 1,
      append: true,
    })
  } finally {
    isLoadingMore.value = false
  }
}
</script>

<template>
  <main class="search-page">
    <section class="hero-panel">
      <div class="hero-copy">
        <p class="eyebrow">Aveon</p>
        <div class="hero-heading">
          <h1>Flight discovery across nearby airports</h1>
          <p class="lead">
            Compare grouped fares, expand flexible dates, and refine results without leaving the page.
          </p>
        </div>
      </div>
    </section>

    <div class="search-bar-wrap">
      <FlightSearchBar
        v-model:origin-input="originInput"
        v-model:destination-input="destinationInput"
        v-model:origin-airports="originAirports"
        v-model:destination-airports="destinationAirports"
        v-model:trip-type="tripType"
        v-model:departure-date-from="departureDateFrom"
        v-model:departure-date-to="departureDateTo"
        v-model:selected-departure-dates="selectedDepartureDates"
        v-model:return-date-from="returnDateFrom"
        v-model:return-date-to="returnDateTo"
        v-model:selected-return-dates="selectedReturnDates"
        v-model:adults="adults"
        v-model:cabin-class="cabinClass"
        :response-exists="Boolean(response)"
        :is-collapsed="isSearchCollapsed"
        :compact-summary="compactSearchSummary"
        :search-combination-count="searchCombinationCount"
        :max-departure-range-days="MAX_DEPARTURE_RANGE_DAYS"
        :loading="loading"
        :origin-suggestions="originSuggestions"
        :destination-suggestions="destinationSuggestions"
        :cabin-options="cabinOptions"
        @submit="searchFlights"
        @toggle-collapse="isSearchCollapsed = !isSearchCollapsed"
        @confirm-origin-input="confirmOriginInput"
        @confirm-destination-input="confirmDestinationInput"
        @remove-origin-airport="removeOriginAirport"
        @remove-destination-airport="removeDestinationAirport"
        @add-origin-airport="addOriginAirport"
        @add-destination-airport="addDestinationAirport"
      />
    </div>

    <p v-if="error" class="error-message">{{ error }}</p>

    <Transition name="progress-shell">
      <section v-if="isPolling && searchSession" class="progress-shell">
        <div class="progress-copy">
          <p class="eyebrow">Search Progress</p>
          <strong>
            {{ searchSession.completedCombinations }} / {{ searchSession.totalCombinations }} combinations
          </strong>
          <span v-if="searchSession.failedCombinations > 0">
            {{ searchSession.failedCombinations }} failed
          </span>
        </div>
        <div class="progress-bar">
          <div class="progress-bar-fill" :style="{ width: `${progressPercentage}%` }" />
        </div>
      </section>
    </Transition>

    <section class="results-grid" :class="{ 'results-only': !response }">
      <SearchFilters
        v-if="response"
        v-model:include-direct-flights="includeDirectFlights"
        v-model:include-one-stop-flights="includeOneStopFlights"
        v-model:include-two-plus-stop-flights="includeTwoPlusStopFlights"
        v-model:selected-providers="selectedProviders"
        v-model:selected-airlines="selectedAirlines"
        v-model:selected-departure-airports="selectedDepartureAirports"
        v-model:selected-arrival-airports="selectedArrivalAirports"
        v-model:departure-time-range="departureTimeRange"
        v-model:arrival-time-range="arrivalTimeRange"
        v-model:max-duration-minutes="maxDurationMinutes"
        :available-max-duration-minutes="availableMaxDurationMinutes"
        :airline-filters="airlineFilters"
        :departure-airport-filters="departureAirportFilters"
        :arrival-airport-filters="arrivalAirportFilters"
        :provider-filters="providerFilters"
      />

      <section class="results-panel">
        <div v-if="response" class="results-shell">
          <div class="results-header">
            <div>
              <p class="eyebrow">Results</p>
              <h2>{{ searchSummary }}</h2>
            </div>
            <div class="results-stats">
              <span
                :title="`Loaded flights: ${filteredResults.length}\nDirect: ${loadedStopCounts.direct}\n1 stop: ${loadedStopCounts.oneStop}\n2+ stops: ${loadedStopCounts.twoPlusStop}`"
              >
                {{ filteredResults.length }} loaded flights
                <template v-if="response.pagination.totalResults > 0">
                  (out of {{ response.pagination.totalResults }})
                </template>
              </span>
              <span>{{ response.metadata.providerResultCount }} provider fares</span>
              <span>{{ response.metadata.searchCombinationCount }} search combinations</span>
            </div>
          </div>

          <TransitionGroup name="result-list" tag="div" class="results-list">
            <SearchResultCard
              v-for="result in filteredResults"
              :key="result.id"
              :result="result"
              :expanded="isExpanded(result.id)"
              @toggle-expanded="toggleExpanded"
            />
          </TransitionGroup>

          <div v-if="response.pagination.totalPages > 1" class="pagination-bar">
            <span class="pagination-summary">{{ paginationSummary }}</span>
            <span class="pagination-page">Page {{ currentPage }} of {{ totalPages }}</span>
          </div>

          <div
            v-if="response.pagination.totalPages > 1 && hasMoreResults"
            ref="loadMoreSentinel"
            class="load-more-sentinel"
            aria-hidden="true"
          />

          <div v-if="isLoadingMore" class="load-more-status">
            Loading more fares…
          </div>
        </div>
      </section>
    </section>
  </main>
</template>

<style scoped src="./FlightSearch.css"></style>
