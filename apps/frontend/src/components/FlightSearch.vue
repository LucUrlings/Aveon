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
  type SearchResponse,
  type SearchSessionResponse,
} from '../features/flight-search/types'

const MAX_DEPARTURE_RANGE_DAYS = 10

const originInput = ref('')
const destinationInput = ref('')
const originAirports = ref<AirportOption[]>([
  { code: 'DUB', name: 'Dublin', displayLabel: 'Dublin (DUB)' },
])
const destinationAirports = ref<AirportOption[]>([
  { code: 'AMS', name: 'Amsterdam Schiphol', displayLabel: 'Amsterdam Schiphol (AMS)' },
])

const departureDateFrom = ref('2026-05-15')
const departureDateTo = ref('2026-05-17')
const selectedDepartureDates = ref<string[]>(['2026-05-15', '2026-05-16', '2026-05-17'])
const adults = ref(1)
const cabinClass = ref('economy')

const originSuggestions = ref<AirportOption[]>([])
const destinationSuggestions = ref<AirportOption[]>([])

const loading = ref(false)
const error = ref<string | null>(null)
const response = ref<SearchResponse | null>(null)
const searchSession = ref<SearchSessionResponse | null>(null)
const expandedResultIds = ref<string[]>([])
const isSearchCollapsed = ref(false)

const includeDirectFlights = ref(true)
const includeOneStopFlights = ref(false)
const includeTwoPlusStopFlights = ref(false)
const selectedProviders = ref<string[]>([])
const selectedAirlines = ref<string[]>([])
const selectedDepartureAirports = ref<string[]>([])
const selectedArrivalAirports = ref<string[]>([])
const priceRange = ref<[number, number]>([0, 0])
const maxDurationMinutes = ref(0)
const departureTimeRange = ref<[number, number]>([0, 1439])
const arrivalTimeRange = ref<[number, number]>([0, 1439])

let originRequestId = 0
let destinationRequestId = 0
let pollingTimer: number | null = null
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
    'price',
    'maxDuration',
    'departureTime',
    'arrivalTime',
  ].some((key) => params[key] !== undefined)

const buildSearchRequestKey = (
  origins: string[],
  destinations: string[],
  dates: string[],
  adultsValue: number,
  cabinClassValue: string,
) => JSON.stringify({
  origins: [...origins].sort((left, right) => left.localeCompare(right)),
  destinations: [...destinations].sort((left, right) => left.localeCompare(right)),
  dates: [...dates].sort((left, right) => left.localeCompare(right)),
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

  return buildSearchRequestKey(origins, destinations, dates, adults.value, cabinClass.value)
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
  priceRange.value = parseRangeParam(getQueryString(params.price), priceRange.value)
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

const updateRouteState = async () => {
  if (!hasMounted || isSyncingRoute) {
    return
  }

  const query: Record<string, string> = {}
  setListParam(query, 'origins', originAirports.value.map((airport) => airport.code))
  setListParam(query, 'destinations', destinationAirports.value.map((airport) => airport.code))
  setListParam(query, 'dates', selectedDepartureDates.value)
  query.adults = String(adults.value)

  if (cabinClass.value !== 'economy') {
    query.cabinClass = cabinClass.value
  }

  setBooleanParam(query, 'direct', includeDirectFlights.value, true)
  setBooleanParam(query, 'oneStop', includeOneStopFlights.value, false)
  setBooleanParam(query, 'twoPlusStop', includeTwoPlusStopFlights.value, false)
  setListParam(query, 'providers', selectedProviders.value)
  setListParam(query, 'airlines', selectedAirlines.value)
  setListParam(query, 'departureAirports', selectedDepartureAirports.value)
  setListParam(query, 'arrivalAirports', selectedArrivalAirports.value)
  setRangeParam(query, 'price', priceRange.value, [0, 0])
  setNumberParam(query, 'maxDuration', maxDurationMinutes.value, 0)
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

  return [...new Set(
    response.value.results.flatMap((result) =>
      result.priceOptions.map((option) => option.provider),
    ),
  )].sort((left, right) => left.localeCompare(right))
})

const airlineFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return [...new Set(
    response.value.results.flatMap((result) =>
      result.legs.flatMap((leg) =>
        leg.segments
          .map((segment) => segment.marketingCarrierName?.trim())
          .filter((name): name is string => Boolean(name)),
      ),
    ),
  )].sort((left, right) => left.localeCompare(right))
})

const departureAirportFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return [...new Set(
    response.value.results
      .map((result) => result.legs[0]?.originAirport?.trim())
      .filter((airport): airport is string => Boolean(airport)),
  )].sort((left, right) => left.localeCompare(right))
})

const arrivalAirportFilters = computed(() => {
  if (!response.value) {
    return []
  }

  return [...new Set(
    response.value.results
      .map((result) => result.legs[result.legs.length - 1]?.destinationAirport?.trim())
      .filter((airport): airport is string => Boolean(airport)),
  )].sort((left, right) => left.localeCompare(right))
})

const getVisiblePriceOptions = (result: SearchResponse['results'][number]) => {
  const visibleOptions = selectedProviders.value.length > 0
    ? result.priceOptions.filter((option) => selectedProviders.value.includes(option.provider))
    : result.priceOptions

  return [...visibleOptions].sort((left, right) => left.totalPrice.amount - right.totalPrice.amount)
}

const baseFilteredResults = computed(() => {
  if (!response.value) {
    return []
  }

  return response.value.results.flatMap((result) => {
    const stopCount = getStopCount(result)
    const includeByStopCount = (
      (stopCount === 0 && includeDirectFlights.value) ||
      (stopCount === 1 && includeOneStopFlights.value) ||
      (stopCount >= 2 && includeTwoPlusStopFlights.value)
    )

    if (!includeByStopCount) {
      return []
    }

    const visiblePriceOptions = getVisiblePriceOptions(result)
    if (visiblePriceOptions.length === 0) {
      return []
    }

    if (selectedAirlines.value.length > 0) {
      const resultAirlines = new Set(
        result.legs.flatMap((leg) =>
          leg.segments
            .map((segment) => segment.marketingCarrierName?.trim())
            .filter((name): name is string => Boolean(name)),
        ),
      )

      const hasSelectedAirline = selectedAirlines.value.some((airline) => resultAirlines.has(airline))
      if (!hasSelectedAirline) {
        return []
      }
    }

    const departureAirport = result.legs[0]?.originAirport?.trim()
    if (
      selectedDepartureAirports.value.length > 0 &&
      (!departureAirport || !selectedDepartureAirports.value.includes(departureAirport))
    ) {
      return []
    }

    const arrivalAirport = result.legs[result.legs.length - 1]?.destinationAirport?.trim()
    if (
      selectedArrivalAirports.value.length > 0 &&
      (!arrivalAirport || !selectedArrivalAirports.value.includes(arrivalAirport))
    ) {
      return []
    }

    const firstDepartureMinutes = getClockMinutes(result.legs[0]?.departureLocalTime)
    if (firstDepartureMinutes < departureTimeRange.value[0] || firstDepartureMinutes > departureTimeRange.value[1]) {
      return []
    }

    const lastArrivalMinutes = getClockMinutes(result.legs[result.legs.length - 1]?.arrivalLocalTime)
    if (lastArrivalMinutes < arrivalTimeRange.value[0] || lastArrivalMinutes > arrivalTimeRange.value[1]) {
      return []
    }

    return [{
      ...result,
      priceOptions: visiblePriceOptions,
    }]
  })
})

const availableMaxDurationMinutes = computed(() => {
  if (baseFilteredResults.value.length === 0) {
    return 0
  }

  return Math.max(...baseFilteredResults.value.map((result) => result.totalDurationMinutes))
})

const availablePriceRange = computed<[number, number]>(() => {
  if (baseFilteredResults.value.length === 0) {
    return [0, 0]
  }

  const amounts = baseFilteredResults.value
    .map((result) => result.priceOptions[0]?.totalPrice.amount ?? 0)
    .filter((amount) => Number.isFinite(amount))

  if (amounts.length === 0) {
    return [0, 0]
  }

  return [Math.floor(Math.min(...amounts)), Math.ceil(Math.max(...amounts))]
})

const filteredResults = computed(() => {
  return baseFilteredResults.value.filter((result) => {
    if (maxDurationMinutes.value > 0 && result.totalDurationMinutes > maxDurationMinutes.value) {
      return false
    }

    const cheapest = result.priceOptions[0]
    if (cheapest.totalPrice.amount < priceRange.value[0] || cheapest.totalPrice.amount > priceRange.value[1]) {
      return false
    }

    return true
  })
})

const searchSummary = computed(() => {
  if (!response.value) {
    return 'Search across multiple airports and compare grouped fares.'
  }

  return `${filteredResults.value.length} flights after filters`
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
  return `${origins} to ${destinations} on ${dateSummary}`
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
    return `Aveon · ${filteredResults.value.length} flights from ${routeSummary}`
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

  return routeCombinationCount * departureDateCount
})

const addDays = (dateString: string, days: number) => {
  const date = new Date(`${dateString}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

const getClockMinutes = (value?: string) => {
  if (!value) {
    return 0
  }

  const match = value.match(/T(\d{2}):(\d{2})/)
  if (!match) {
    return 0
  }

  const hours = Number.parseInt(match[1], 10)
  const minutes = Number.parseInt(match[2], 10)

  return (hours * 60) + minutes
}

const getStopCount = (result: SearchResponse['results'][number]) =>
  result.legs.reduce((totalStops, leg) => totalStops + Math.max(leg.segments.length - 1, 0), 0)

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

const resetPriceRangeToAvailable = () => {
  const [minPrice, maxPrice] = availablePriceRange.value
  priceRange.value = [minPrice, maxPrice]
}

const resetMaxDurationToAvailable = () => {
  maxDurationMinutes.value = availableMaxDurationMinutes.value
}

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
    selectedItems.value = [...availableItems]
    return
  }

  selectedItems.value = selectedItems.value.filter((item) => availableItems.includes(item))
}

const syncPriceRangeToAvailable = (shouldReset: boolean) => {
  const [availableMinPrice, availableMaxPrice] = availablePriceRange.value

  if (shouldReset) {
    priceRange.value = [availableMinPrice, availableMaxPrice]
    return
  }

  const nextMinPrice = Math.min(
    Math.max(priceRange.value[0], availableMinPrice),
    availableMaxPrice,
  )
  const nextMaxPrice = Math.max(
    Math.min(priceRange.value[1], availableMaxPrice),
    nextMinPrice,
  )

  priceRange.value = [nextMinPrice, nextMaxPrice]
}

const syncMaxDurationToAvailable = (shouldReset: boolean) => {
  if (shouldReset) {
    maxDurationMinutes.value = availableMaxDurationMinutes.value
    return
  }

  maxDurationMinutes.value = Math.min(maxDurationMinutes.value, availableMaxDurationMinutes.value)
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

watch(
  [
    includeDirectFlights,
    includeOneStopFlights,
    includeTwoPlusStopFlights,
    selectedProviders,
    selectedAirlines,
    selectedDepartureAirports,
    selectedArrivalAirports,
    departureTimeRange,
    arrivalTimeRange,
  ],
  () => {
    resetPriceRangeToAvailable()
    resetMaxDurationToAvailable()
  },
  { immediate: true, deep: true },
)

watch(
  response,
  (nextResponse, previousResponse) => {
    const shouldResetFilters = !previousResponse && Boolean(nextResponse) && !hasHydratedFiltersFromUrl
    const previousProviderFilters = previousResponse
      ? [...new Set(
        previousResponse.results.flatMap((result) =>
          result.priceOptions.map((option) => option.provider),
        ),
      )].sort((left, right) => left.localeCompare(right))
      : []
    const previousAirlineFilters = previousResponse
      ? [...new Set(
        previousResponse.results.flatMap((result) =>
          result.legs.flatMap((leg) =>
            leg.segments
              .map((segment) => segment.marketingCarrierName?.trim())
              .filter((name): name is string => Boolean(name)),
          ),
        ),
      )].sort((left, right) => left.localeCompare(right))
      : []
    const previousDepartureAirportFilters = previousResponse
      ? [...new Set(
        previousResponse.results
          .map((result) => result.legs[0]?.originAirport?.trim())
          .filter((airport): airport is string => Boolean(airport)),
      )].sort((left, right) => left.localeCompare(right))
      : []
    const previousArrivalAirportFilters = previousResponse
      ? [...new Set(
        previousResponse.results
          .map((result) => result.legs[result.legs.length - 1]?.destinationAirport?.trim())
          .filter((airport): airport is string => Boolean(airport)),
      )].sort((left, right) => left.localeCompare(right))
      : []

    syncSelectedFiltersToAvailable(selectedProviders, providerFilters.value, previousProviderFilters, shouldResetFilters)
    syncSelectedFiltersToAvailable(selectedAirlines, airlineFilters.value, previousAirlineFilters, shouldResetFilters)
    syncSelectedFiltersToAvailable(selectedDepartureAirports, departureAirportFilters.value, previousDepartureAirportFilters, shouldResetFilters)
    syncSelectedFiltersToAvailable(selectedArrivalAirports, arrivalAirportFilters.value, previousArrivalAirportFilters, shouldResetFilters)
    syncPriceRangeToAvailable(shouldResetFilters)
    syncMaxDurationToAvailable(shouldResetFilters)
    hasHydratedFiltersFromUrl = false
  },
  { immediate: true },
)

watch(
  [
    originAirports,
    destinationAirports,
    selectedDepartureDates,
    adults,
    cabinClass,
    includeDirectFlights,
    includeOneStopFlights,
    includeTwoPlusStopFlights,
    selectedProviders,
    selectedAirlines,
    selectedDepartureAirports,
    selectedArrivalAirports,
    priceRange,
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

onMounted(() => {
  applyUrlState()
  hasMounted = true
  void updateRouteState()
  void fetchAirportSuggestions(originAirports.value[0].code)
  void fetchAirportSuggestions(destinationAirports.value[0].code)
  syncSearchFromRoute()
})

onBeforeUnmount(() => {
  stopPolling()
})

const stopPolling = () => {
  if (pollingTimer !== null) {
    window.clearTimeout(pollingTimer)
    pollingTimer = null
  }
}

const pollSearchSession = async (searchId: string) => {
  try {
    const session = await getSearchSession(searchId)
    searchSession.value = session
    response.value = session.response

    if (session.errorMessage) {
      error.value = session.errorMessage
    }

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
  expandedResultIds.value = []

  try {
    lastExecutedSearchKey = getCurrentSearchRequestKey()

    const request: SearchRequest = {
      originAirports: originAirports.value.map((airport) => airport.code),
      destinationAirports: destinationAirports.value.map((airport) => airport.code),
      selectedDates: [...selectedDepartureDates.value],
      returnDateFrom: null,
      returnDateTo: null,
      adults: adults.value,
      cabinClass: cabinClass.value,
    }

    const session = await searchFlightsRequest(request)
    searchSession.value = session
    response.value = session.response
    isSearchCollapsed.value = true
    loading.value = false

    if (session.status === 'running') {
      await pollSearchSession(session.searchId)
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
        v-model:departure-date-from="departureDateFrom"
        v-model:departure-date-to="departureDateTo"
        v-model:selected-departure-dates="selectedDepartureDates"
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
        v-model:price-range="priceRange"
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
        :available-price-range="availablePriceRange"
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
                :title="`Flight options: ${response.metadata.returnedResultCount}\nDirect: ${response.metadata.returnedDirectFlightCount}\n1 stop: ${response.metadata.returnedOneStopFlightCount}\n2+ stops: ${response.metadata.returnedTwoPlusStopFlightCount}`"
              >
                {{ response.metadata.returnedResultCount }} flight options
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
        </div>
      </section>
    </section>
  </main>
</template>

<style scoped src="./FlightSearch.css"></style>
