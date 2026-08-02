import { ref, watch, type Ref } from 'vue'
import { useRoute, useRouter, type LocationQuery } from 'vue-router'
import type { AirportOption, SearchResponse } from './types'
import {
  buildSearchRequestKey,
  getExplicitSelection,
  getQueryString,
  parseBooleanParam,
  parseCodeListParam,
  parseDateListParam,
  parseNumberParam,
  parseRangeParam,
  parseStringListParam,
  setBooleanParam,
  setListParam,
  setNumberParam,
  setRangeParam,
} from './searchRoute'

type TripType = 'oneWay' | 'return'
type TimeRange = [number, number]
type ReadonlyRef<T> = Readonly<Ref<T>>

type SearchRouteStateOptions = {
  originAirports: Ref<AirportOption[]>
  destinationAirports: Ref<AirportOption[]>
  selectedDepartureDates: Ref<string[]>
  departureDateFrom: Ref<string | null>
  departureDateTo: Ref<string | null>
  tripType: Ref<TripType>
  selectedReturnDates: Ref<string[]>
  returnDateFrom: Ref<string | null>
  returnDateTo: Ref<string | null>
  adults: Ref<number>
  cabinClass: Ref<string>
  includeDirectFlights: Ref<boolean>
  includeOneStopFlights: Ref<boolean>
  includeTwoPlusStopFlights: Ref<boolean>
  selectedProviders: Ref<string[]>
  selectedAirlines: Ref<string[]>
  selectedDepartureAirports: Ref<string[]>
  selectedArrivalAirports: Ref<string[]>
  maxDurationMinutes: Ref<number>
  departureTimeRange: Ref<TimeRange>
  arrivalTimeRange: Ref<TimeRange>
  returnDepartureTimeRange: Ref<TimeRange>
  returnArrivalTimeRange: Ref<TimeRange>
  selectedOutboundLegId: Ref<string | null>
  selectedReturnLegId: Ref<string | null>
  response: ReadonlyRef<SearchResponse | null>
  providerFilters: ReadonlyRef<string[]>
  airlineFilters: ReadonlyRef<string[]>
  departureAirportFilters: ReadonlyRef<string[]>
  arrivalAirportFilters: ReadonlyRef<string[]>
  availableMaxDurationMinutes: ReadonlyRef<number>
  lastExecutedSearchKey: ReadonlyRef<string | null>
  search: () => void | Promise<void>
  onReady: () => void
}

const filterQueryKeys = [
  'direct', 'oneStop', 'twoPlusStop', 'providers', 'airlines',
  'departureAirports', 'arrivalAirports', 'maxDuration', 'departureTime',
  'arrivalTime', 'returnDepartureTime', 'returnArrivalTime',
  'outboundLegId', 'returnLegId',
]

const hasActiveFilterQuery = (query: LocationQuery) =>
  filterQueryKeys.some((key) => query[key] !== undefined)

const buildAirportOption = (code: string): AirportOption => ({
  code,
  name: null,
  displayLabel: code,
})

const getSearchRequestKeyFromQuery = (query: LocationQuery) => {
  const origins = parseCodeListParam(getQueryString(query.origins))
  const destinations = parseCodeListParam(getQueryString(query.destinations))
  const dates = parseDateListParam(getQueryString(query.dates))
  if (origins.length === 0 || destinations.length === 0 || dates.length === 0) return null

  return buildSearchRequestKey(
    origins,
    destinations,
    dates,
    getQueryString(query.tripType) === 'return' ? 'return' : 'oneWay',
    parseDateListParam(getQueryString(query.returnDates)),
    parseNumberParam(getQueryString(query.adults), 1),
    getQueryString(query.cabinClass)?.trim() || 'economy',
  )
}

export const useSearchRouteState = (options: SearchRouteStateOptions) => {
  const route = useRoute()
  const router = useRouter()
  const hasHydratedFiltersFromUrl = ref(false)
  const prefillOnly = ref(false)
  let isReady = false
  let isSyncingRoute = false

  const applyUrlState = () => {
    const query = route.query
    prefillOnly.value = getQueryString(query.prefill) === 'true'
    const origins = parseCodeListParam(getQueryString(query.origins))
    const destinations = parseCodeListParam(getQueryString(query.destinations))
    const departureDates = parseDateListParam(getQueryString(query.dates))

    if (origins.length > 0) options.originAirports.value = origins.map(buildAirportOption)
    if (destinations.length > 0) options.destinationAirports.value = destinations.map(buildAirportOption)
    if (departureDates.length > 0) {
      options.selectedDepartureDates.value = departureDates
      options.departureDateFrom.value = departureDates[0]
      options.departureDateTo.value = departureDates.at(-1) ?? null
    }

    options.tripType.value = getQueryString(query.tripType) === 'return' ? 'return' : 'oneWay'
    const returnDates = parseDateListParam(getQueryString(query.returnDates))
    options.selectedReturnDates.value = returnDates
    options.returnDateFrom.value = returnDates[0] ?? null
    options.returnDateTo.value = returnDates.at(-1) ?? null
    options.adults.value = parseNumberParam(getQueryString(query.adults), options.adults.value)
    options.cabinClass.value = getQueryString(query.cabinClass)?.trim() || options.cabinClass.value
    options.includeDirectFlights.value = parseBooleanParam(getQueryString(query.direct), options.includeDirectFlights.value)
    options.includeOneStopFlights.value = parseBooleanParam(getQueryString(query.oneStop), options.includeOneStopFlights.value)
    options.includeTwoPlusStopFlights.value = parseBooleanParam(getQueryString(query.twoPlusStop), options.includeTwoPlusStopFlights.value)
    options.selectedProviders.value = parseStringListParam(getQueryString(query.providers))
    options.selectedAirlines.value = parseStringListParam(getQueryString(query.airlines))
    options.selectedDepartureAirports.value = parseCodeListParam(getQueryString(query.departureAirports))
    options.selectedArrivalAirports.value = parseCodeListParam(getQueryString(query.arrivalAirports))
    options.maxDurationMinutes.value = parseNumberParam(getQueryString(query.maxDuration), options.maxDurationMinutes.value)
    options.departureTimeRange.value = parseRangeParam(getQueryString(query.departureTime), options.departureTimeRange.value)
    options.arrivalTimeRange.value = parseRangeParam(getQueryString(query.arrivalTime), options.arrivalTimeRange.value)
    options.returnDepartureTimeRange.value = parseRangeParam(getQueryString(query.returnDepartureTime), options.returnDepartureTimeRange.value)
    options.returnArrivalTimeRange.value = parseRangeParam(getQueryString(query.returnArrivalTime), options.returnArrivalTimeRange.value)
    options.selectedOutboundLegId.value = getQueryString(query.outboundLegId)
    options.selectedReturnLegId.value = getQueryString(query.returnLegId)
    hasHydratedFiltersFromUrl.value = hasActiveFilterQuery(query)
  }

  const buildQuery = () => {
    const query: Record<string, string> = {}
    setListParam(query, 'origins', options.originAirports.value.map((airport) => airport.code))
    setListParam(query, 'destinations', options.destinationAirports.value.map((airport) => airport.code))
    setListParam(query, 'dates', options.selectedDepartureDates.value)
    if (options.tripType.value === 'return') {
      query.tripType = 'return'
      setListParam(query, 'returnDates', options.selectedReturnDates.value)
    }
    query.adults = String(options.adults.value)
    if (options.cabinClass.value !== 'economy') query.cabinClass = options.cabinClass.value

    setBooleanParam(query, 'direct', options.includeDirectFlights.value, true)
    setBooleanParam(query, 'oneStop', options.includeOneStopFlights.value, false)
    setBooleanParam(query, 'twoPlusStop', options.includeTwoPlusStopFlights.value, false)
    setListParam(query, 'providers', getExplicitSelection(options.selectedProviders.value, options.providerFilters.value))
    setListParam(query, 'airlines', getExplicitSelection(options.selectedAirlines.value, options.airlineFilters.value))
    setListParam(query, 'departureAirports', getExplicitSelection(options.selectedDepartureAirports.value, options.departureAirportFilters.value))
    setListParam(query, 'arrivalAirports', getExplicitSelection(options.selectedArrivalAirports.value, options.arrivalAirportFilters.value))
    setNumberParam(query, 'maxDuration', options.maxDurationMinutes.value, options.response.value ? options.availableMaxDurationMinutes.value : 0)
    setRangeParam(query, 'departureTime', options.departureTimeRange.value, [0, 1439])
    setRangeParam(query, 'arrivalTime', options.arrivalTimeRange.value, [0, 1439])

    if (options.tripType.value === 'return') {
      setRangeParam(query, 'returnDepartureTime', options.returnDepartureTimeRange.value, [0, 1439])
      setRangeParam(query, 'returnArrivalTime', options.returnArrivalTimeRange.value, [0, 1439])
      if (options.selectedOutboundLegId.value) query.outboundLegId = options.selectedOutboundLegId.value
      if (options.selectedReturnLegId.value) query.returnLegId = options.selectedReturnLegId.value
    }
    if (prefillOnly.value) query.prefill = 'true'
    return query
  }

  const updateRouteState = async () => {
    if (!isReady || isSyncingRoute) return
    isSyncingRoute = true
    try {
      await router.replace({ query: buildQuery() })
    } finally {
      isSyncingRoute = false
    }
  }

  const syncSearchFromRoute = () => {
    if (prefillOnly.value) return
    const routeSearchKey = getSearchRequestKeyFromQuery(route.query)
    if (routeSearchKey && routeSearchKey !== options.lastExecutedSearchKey.value) void options.search()
  }

  const routeStateRefs = [
    options.originAirports, options.destinationAirports, options.tripType,
    options.selectedDepartureDates, options.returnDateFrom, options.returnDateTo,
    options.selectedReturnDates, options.adults, options.cabinClass,
    options.includeDirectFlights, options.includeOneStopFlights, options.includeTwoPlusStopFlights,
    options.selectedProviders, options.selectedAirlines, options.selectedDepartureAirports,
    options.selectedArrivalAirports, options.maxDurationMinutes, options.departureTimeRange,
    options.arrivalTimeRange, options.returnDepartureTimeRange, options.returnArrivalTimeRange,
    options.selectedOutboundLegId, options.selectedReturnLegId,
  ]

  watch(routeStateRefs, () => void updateRouteState(), { deep: true })
  watch(
    () => route.query,
    () => {
      if (isSyncingRoute) return
      applyUrlState()
      syncSearchFromRoute()
    },
  )

  const initialize = () => {
    applyUrlState()
    isReady = true
    options.onReady()
    void updateRouteState()
    syncSearchFromRoute()
  }

  const consumePrefill = () => {
    if (!prefillOnly.value) return
    prefillOnly.value = false
    void updateRouteState()
  }

  return { hasHydratedFiltersFromUrl, initialize, consumePrefill }
}
