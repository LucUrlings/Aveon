import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AirportGroupPicker from '../../../src/components/flight-search/AirportGroupPicker.vue'
import OptimizedTripSearch from '../../../src/features/itinerary-search/OptimizedTripSearch.vue'

const { startSearch, getSearch, cancelSearch, getCapabilities } = vi.hoisted(() => ({
  startSearch: vi.fn(),
  getSearch: vi.fn(),
  cancelSearch: vi.fn(),
  getCapabilities: vi.fn(),
}))

vi.mock('../../../src/features/itinerary-search/api', () => ({
  startItinerarySearch: startSearch,
  getItinerarySearch: getSearch,
  cancelItinerarySearch: cancelSearch,
  getItinerarySearchCapabilities: getCapabilities,
}))

const airport = (code: string) => ({ code, name: `${code} Airport`, displayLabel: `${code} Airport` })
const coverage = { mode: 'exhaustive', liveProviderCallsUsed: 4, providerCallLimit: 25, cacheHits: 3, candidateStatesEvaluated: 12, candidateStatesPruned: 2 }
const result = {
  id: 'trip-1', bookingType: 'separateTickets', destinationOrder: ['AMS'], totalPrice: 210, currency: 'EUR', totalFlightDurationMinutes: 150,
  totalStops: 0, bookingCount: 1, airportSwitches: 0, stays: [{ destinationId: 'AMS', arrivalDate: '2026-08-03', departureDate: '2026-08-05', nights: 2 }],
  legs: [{ id: 'leg-1', originAirport: 'DUB', destinationAirport: 'AMS', departureLocalTime: '2026-08-03T08:00:00Z', arrivalLocalTime: '2026-08-03T10:30:00Z', durationMinutes: 150, stops: 0, segments: [] }],
  bookingOptions: [{ label: 'Book', url: 'https://example.test/book', price: 210, currency: 'EUR', provider: 'FlightAPI' }],
  warnings: [{ code: 'separate-tickets', message: 'Separate tickets are not protected connections.' }],
  rankingBreakdown: { score: 1, totalPrice: 210, additionalFlightMinutes: 0, totalStops: 0, additionalBookings: 0, airportSwitches: 0 },
}
const filters = { airlines: [], bookingSources: [{ value: 'FlightAPI', label: 'FlightAPI', count: 1 }], departureAirports: [], arrivalAirports: [], maxPrice: 210, maxDurationMinutes: 150, maxBookingCount: 1, maxAirportSwitches: 0 }
const session = (overrides = {}) => ({ searchId: 'optimizer-1', mode: 'optimize', status: 'completed', phase: 'completed', progress: 100, coverage, results: [], warnings: [], filters, pagination: { page: 1, pageSize: 10, totalResults: 0, totalPages: 0 }, feasibility: { requiredLegCount: 2, minimumCalendarDays: 3, availableCalendarDays: 8, routeOrderCount: 1, generatedScheduleCount: 2, bounded: false }, ...overrides })

const configureTrip = async (wrapper: ReturnType<typeof mount<typeof OptimizedTripSearch>>) => {
  const pickers = wrapper.findAllComponents(AirportGroupPicker)
  pickers[0].vm.$emit('update:airports', [airport('DUB')])
  pickers[1].vm.$emit('update:airports', [airport('AMS')])
  await wrapper.vm.$nextTick()
}

describe('OptimizedTripSearch', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.history.replaceState({}, '', '/')
    cancelSearch.mockResolvedValue(undefined)
    getCapabilities.mockResolvedValue({ providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 8 })
    startSearch.mockResolvedValue(session())
    getSearch.mockResolvedValue(session())
  })

  it('shows preliminary leg, calendar-length, search-size, and impossible-window feedback', async () => {
    const wrapper = mount(OptimizedTripSearch)

    expect(wrapper.get('.feasibility').text()).toContain('2 inter-city legs')
    expect(wrapper.get('.feasibility').text()).toContain('3 minimum calendar days')
    expect(wrapper.get('.feasibility').text()).toContain('airport-route checks')
    expect(wrapper.get('.feasibility').text()).toContain('25-call allowance')
    const dates = wrapper.findAll('input[type="date"]')
    const tripStart = (dates[0].element as HTMLInputElement).value
    await dates[1].setValue(tripStart)

    expect(wrapper.get('.feasibility [role="alert"]').text()).toContain('needs at least 3')
    expect(wrapper.get('button.primary-action').attributes('disabled')).toBeDefined()
  })

  it('supports every endpoint mode and serializes fixed-end and stay rules', async () => {
    const wrapper = mount(OptimizedTripSearch)
    const endpoint = wrapper.get('[aria-label="Trip endpoint mode"]')

    await endpoint.setValue('openEnded')
    expect(wrapper.get('.feasibility').text()).toContain('1 inter-city legs')
    await endpoint.setValue('fixedEnd')
    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(3)

    const pickers = wrapper.findAllComponents(AirportGroupPicker)
    pickers[0].vm.$emit('update:airports', [airport('DUB')])
    pickers[1].vm.$emit('update:airports', [airport('LHR')])
    pickers[2].vm.$emit('update:airports', [airport('AMS')])
    await wrapper.get('[aria-label="Destination 1 group name"]').setValue('Netherlands')
    await wrapper.get('[aria-label="Destination 1 stay rule"]').setValue('exactNights')
    await wrapper.get('[aria-label="Destination 1 nights"]').setValue(2)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(startSearch).toHaveBeenCalledOnce()
    expect(startSearch.mock.calls[0][0]).toMatchObject({
      mode: 'optimize', endpointMode: 'fixedEnd',
      start: { label: 'Starting point', airportCodes: ['DUB'] }, fixedEnd: { label: 'Final stop', airportCodes: ['LHR'] },
      destinations: [{ group: { label: 'Netherlands', airportCodes: ['AMS'] }, stay: { mode: 'exactNights', nights: 2 } }],
    })
    expect(wrapper.get('.authoritative-feasibility').text()).toContain('2 valid abstract schedules')
  })

  it('uses server-provided destination, airport, and trip-length limits', async () => {
    getCapabilities.mockResolvedValue({ providerCallLimit: 40, maxOptimizedDestinations: 2, maxAirportsPerGroup: 1, maxTripDays: 3, maxOrderedLegs: 4 })
    const wrapper = mount(OptimizedTripSearch)
    await flushPromises()

    const addDestination = wrapper.get('button.secondary-action')
    await addDestination.trigger('click')
    expect(wrapper.findAll('fieldset.optimized-destination')).toHaveLength(2)
    expect(addDestination.attributes('disabled')).toBeDefined()
    expect(wrapper.findAllComponents(AirportGroupPicker).every(picker => picker.props('maxAirports') === 1)).toBe(true)
    expect(wrapper.get('.feasibility').text()).toContain('40-call allowance')

    const dates = wrapper.findAll('input[type="date"]')
    const start = new Date(`${(dates[0].element as HTMLInputElement).value}T00:00:00Z`)
    start.setUTCDate(start.getUTCDate() + 3)
    await dates[1].setValue(start.toISOString().slice(0, 10))
    expect(wrapper.get('.feasibility [role="alert"]').text()).toContain('current limit is 3')
  })

  it('resumes a session and renders progressive coverage, shared filters, timelines, and warnings before booking', async () => {
    window.history.replaceState({}, '', '/multi-destination?searchId=optimizer-1')
    getSearch.mockResolvedValue(session({ results: [result], warnings: [{ code: 'bounded', message: 'The best trips found within the search allowance are shown.' }], pagination: { page: 1, pageSize: 10, totalResults: 1, totalPages: 1 } }))
    const wrapper = mount(OptimizedTripSearch)
    await flushPromises()

    expect(getSearch).toHaveBeenCalledWith('optimizer-1', expect.objectContaining({ ranking: 'recommended' }), expect.any(AbortSignal))
    expect(wrapper.get('.search-progress').text()).toContain('Exhaustive')
    expect(wrapper.get('.session-warnings').attributes('aria-live')).toBe('assertive')
    expect(wrapper.get('[aria-label="Itinerary filters"]').text()).toContain('Maximum bookings')
    expect(wrapper.get('[aria-label="Complete itinerary timeline"]').text()).toContain('DUB → AMS')
    expect(wrapper.get('.result-card').text().indexOf('Before you book')).toBeLessThan(wrapper.get('.result-card').text().indexOf('Book flight 1'))
    expect(wrapper.findAll('[role="tab"]')).toHaveLength(3)
    await wrapper.get('[aria-selected="true"]').trigger('keydown', { key: 'ArrowRight' })
    await flushPromises()
    expect(wrapper.get('#ranking-tab-cheapest').attributes('aria-selected')).toBe('true')
  })

  it('cancels the running session before starting a replacement search', async () => {
    vi.useFakeTimers()
    let releaseStale!: (value: ReturnType<typeof session>) => void
    const staleResponse = new Promise<ReturnType<typeof session>>(resolve => { releaseStale = resolve })
    startSearch.mockResolvedValueOnce(session({ status: 'running', phase: 'searchingEdges', progress: 20 })).mockResolvedValueOnce(session({ searchId: 'optimizer-2', warnings: [{ code: 'replacement', message: 'Replacement search' }] }))
    getSearch.mockReturnValueOnce(staleResponse)
    const wrapper = mount(OptimizedTripSearch)
    await configureTrip(wrapper)
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    await vi.advanceTimersByTimeAsync(50)
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    releaseStale(session({ warnings: [{ code: 'stale', message: 'Stale search' }] }))
    await flushPromises()

    expect(cancelSearch).toHaveBeenCalledWith('optimizer-1')
    expect(startSearch).toHaveBeenCalledTimes(2)
    expect(new URL(window.location.href).searchParams.get('searchId')).toBe('optimizer-2')
    expect(wrapper.text()).toContain('Replacement search')
    expect(wrapper.text()).not.toContain('Stale search')
    wrapper.unmount()
    vi.useRealTimers()
  })

  it('lets the user cancel a running search', async () => {
    window.history.replaceState({}, '', '/multi-destination?searchId=optimizer-1')
    getSearch.mockResolvedValue(session({ status: 'running', phase: 'buildingItineraries', progress: 65 }))
    const wrapper = mount(OptimizedTripSearch)
    await flushPromises()
    await wrapper.get('.progress-actions button').trigger('click')
    await flushPromises()

    expect(cancelSearch).toHaveBeenCalledWith('optimizer-1')
    expect(wrapper.get('.empty-state').text()).toContain('Search canceled')
  })

  it('loads the next page without replacing already displayed itineraries', async () => {
    window.history.replaceState({}, '', '/multi-destination?searchId=optimizer-1')
    const secondResult = { ...result, id: 'trip-2', totalPrice: 240 }
    getSearch.mockImplementation((_id, query) => Promise.resolve(query?.page === 2
      ? session({ results: [secondResult], pagination: { page: 2, pageSize: 1, totalResults: 2, totalPages: 2 } })
      : session({ results: [result], pagination: { page: 1, pageSize: 1, totalResults: 2, totalPages: 2 } })))
    const wrapper = mount(OptimizedTripSearch)
    await flushPromises()
    await wrapper.get('.load-more-sentinel button').trigger('click')
    await flushPromises()

    expect(wrapper.findAllComponents({ name: 'ItineraryResultCard' })).toHaveLength(2)
  })

  it('offers an in-place retry after a polling failure', async () => {
    vi.useFakeTimers()
    window.history.replaceState({}, '', '/multi-destination?searchId=optimizer-1')
    getSearch.mockResolvedValueOnce(session({ status: 'running', phase: 'searchingEdges', progress: 20 })).mockRejectedValueOnce(new Error('Temporary connection problem')).mockResolvedValue(session())
    const wrapper = mount(OptimizedTripSearch)
    await flushPromises()
    await vi.advanceTimersByTimeAsync(300)
    await flushPromises()

    expect(wrapper.get('.progress-actions').text()).toContain('Retry loading search')
    await wrapper.findAll('.progress-actions button').find(button => button.text().includes('Retry'))!.trigger('click')
    await flushPromises()
    expect(wrapper.find('.form-error').exists()).toBe(false)
    wrapper.unmount()
    vi.useRealTimers()
  })
})
