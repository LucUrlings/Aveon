import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import FlightSearch from '../../../src/components/FlightSearch.vue'
import type { SearchSessionResponse } from '../../../src/features/flight-search/types'

const {
  mockFetchAirportSuggestions,
  mockGetSearchSession,
  mockSearchFlightsRequest,
  intersectionObserverCallbacks,
} = vi.hoisted(() => ({
  mockFetchAirportSuggestions: vi.fn(),
  mockGetSearchSession: vi.fn(),
  mockSearchFlightsRequest: vi.fn(),
  intersectionObserverCallbacks: [] as Array<IntersectionObserverCallback>,
}))

const mountedWrappers: Array<ReturnType<typeof mount>> = []

vi.mock('../../../src/features/flight-search/api', () => ({
  fetchAirportSuggestions: mockFetchAirportSuggestions,
  getSearchSession: mockGetSearchSession,
  searchFlightsRequest: mockSearchFlightsRequest,
}))

const getDateAheadOfToday = (daysAhead: number) => {
  const today = new Date()
  return new Date(Date.UTC(
    today.getFullYear(),
    today.getMonth(),
    today.getDate() + daysAhead,
  )).toISOString().slice(0, 10)
}

const getDefaultDepartureDates = () => [
  getDateAheadOfToday(7),
  getDateAheadOfToday(8),
  getDateAheadOfToday(9),
]

const makeSession = (overrides: Partial<SearchSessionResponse> = {}): SearchSessionResponse => ({
  searchId: 'search-1',
  status: 'completed',
  totalCombinations: 3,
  completedCombinations: 3,
  failedCombinations: 0,
  response: {
    metadata: {
      searchCombinationCount: 3,
      providerResultCount: 2,
      returnedResultCount: 2,
      returnedDirectFlightCount: 2,
      returnedOneStopFlightCount: 0,
      returnedTwoPlusStopFlightCount: 0,
    },
    filters: {
      providers: [{ value: 'FlightApi:KLM', count: 2 }],
      airlines: [{ value: 'KLM', count: 2 }],
      departureAirports: [{ value: 'AMS', count: 2 }],
      arrivalAirports: [{ value: 'DUB', count: 2 }],
      durationMinutes: { min: 90, max: 90 },
      departureTimeMinutes: { min: 540, max: 1200 },
      arrivalTimeMinutes: { min: 630, max: 1290 },
      returnDepartureTimeMinutes: { min: 0, max: 0 },
      returnArrivalTimeMinutes: { min: 0, max: 0 },
      stops: { direct: 2, oneStop: 0, twoPlusStop: 0 },
    },
    pagination: {
      page: 1,
      pageSize: 100,
      totalResults: 2,
      totalPages: 1,
    },
    results: [
      {
        id: 'morning',
        isRoundTrip: false,
        totalDurationMinutes: 90,
        legs: [
          {
            id: 'morning-leg',
            originAirport: 'AMS',
            destinationAirport: 'DUB',
            departureLocalTime: '2026-05-15T09:00:00',
            arrivalLocalTime: '2026-05-15T10:30:00',
            durationMinutes: 90,
            segments: [
              {
                marketingCarrierName: 'KLM',
                marketingCarrierCode: 'KL',
                flightNumber: '100',
                originAirport: 'AMS',
                destinationAirport: 'DUB',
                departureLocalTime: '2026-05-15T09:00:00',
                arrivalLocalTime: '2026-05-15T10:30:00',
                durationMinutes: 90,
              },
            ],
          },
        ],
        priceOptions: [
          {
            id: 'p1',
            provider: 'FlightApi:KLM',
            totalPrice: { amount: 120, currency: 'EUR' },
            bookingLinks: [{ label: 'View fare', url: 'https://example.com/1' }],
          },
        ],
      },
      {
        id: 'evening',
        isRoundTrip: false,
        totalDurationMinutes: 90,
        legs: [
          {
            id: 'evening-leg',
            originAirport: 'AMS',
            destinationAirport: 'DUB',
            departureLocalTime: '2026-05-15T20:00:00',
            arrivalLocalTime: '2026-05-15T21:30:00',
            durationMinutes: 90,
            segments: [
              {
                marketingCarrierName: 'KLM',
                marketingCarrierCode: 'KL',
                flightNumber: '200',
                originAirport: 'AMS',
                destinationAirport: 'DUB',
                departureLocalTime: '2026-05-15T20:00:00',
                arrivalLocalTime: '2026-05-15T21:30:00',
                durationMinutes: 90,
              },
            ],
          },
        ],
        priceOptions: [
          {
            id: 'p2',
            provider: 'FlightApi:KLM',
            totalPrice: { amount: 150, currency: 'EUR' },
            bookingLinks: [{ label: 'View fare', url: 'https://example.com/2' }],
          },
        ],
      },
    ],
  },
  errorMessage: null,
  ...overrides,
})

beforeEach(() => {
  mockFetchAirportSuggestions.mockReset()
  mockGetSearchSession.mockReset()
  mockSearchFlightsRequest.mockReset()
  intersectionObserverCallbacks.length = 0
  mockFetchAirportSuggestions.mockResolvedValue([])
  mockGetSearchSession.mockResolvedValue(makeSession())
  document.title = ''

  class MockIntersectionObserver {
    private readonly callback: IntersectionObserverCallback

    constructor(callback: IntersectionObserverCallback) {
      this.callback = callback
      intersectionObserverCallbacks.push(callback)
    }

    observe() {}
    unobserve() {}
    disconnect() {}
    takeRecords() { return [] }
    readonly root = null
    readonly rootMargin = '0px'
    readonly thresholds = [0]
  }

  vi.stubGlobal('IntersectionObserver', MockIntersectionObserver)
})

const mountWithRouter = async (initialPath = '/', options: Parameters<typeof mount>[1] = {}) => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: FlightSearch }],
  })

  await router.push(initialPath)
  await router.isReady()

  const wrapper = mount(FlightSearch, {
    ...options,
    global: {
      ...(options.global ?? {}),
      plugins: [...(options.global?.plugins ?? []), router],
    },
  })

  await flushPromises()
  mountedWrappers.push(wrapper)

  return { wrapper, router }
}

afterEach(() => {
  for (const wrapper of mountedWrappers.splice(0)) {
    wrapper.unmount()
  }
})

describe('FlightSearch', () => {
  it('submits exact departure dates to the backend and passes combination count to the search bar', async () => {
    mockSearchFlightsRequest.mockResolvedValue(makeSession())

    const { wrapper, router } = await mountWithRouter('/', {
      global: {
        stubs: {
          FlightSearchBar: {
            props: [
              'searchCombinationCount',
            ],
            emits: ['submit'],
            template: `
              <div>
                <span class="combination-prop">{{ searchCombinationCount }}</span>
                <button class="submit-search" @click="$emit('submit')">submit</button>
              </div>
            `,
          },
          SearchFilters: true,
          SearchResultCard: true,
        },
      },
    })

    await wrapper.get('.submit-search').trigger('click')
    await flushPromises()

    expect(wrapper.get('.combination-prop').text()).toBe('3')
    expect(mockSearchFlightsRequest).toHaveBeenCalledWith(expect.objectContaining({
      departureDates: getDefaultDepartureDates(),
      returnDates: [],
    }), expect.any(AbortSignal))
    expect(mockGetSearchSession).toHaveBeenCalledWith('search-1', expect.objectContaining({
      direct: true,
      oneStop: false,
      twoPlusStop: false,
    }), expect.any(AbortSignal))
    expect(router.currentRoute.value.query.adults).toBe('1')
  })

  it('refetches the current session with backend filter params and updates the page title from filtered results', async () => {
    mockSearchFlightsRequest.mockResolvedValue(makeSession())
    const filteredSession = makeSession({
      response: {
        ...makeSession().response,
        metadata: {
          ...makeSession().response.metadata,
          providerResultCount: 1,
          returnedResultCount: 1,
          returnedDirectFlightCount: 1,
        },
        filters: {
          ...makeSession().response.filters,
          departureTimeMinutes: { min: 540, max: 540 },
        },
        pagination: {
          page: 1,
          pageSize: 100,
          totalResults: 1,
          totalPages: 1,
        },
        results: [makeSession().response.results[0]],
      },
    })
    mockGetSearchSession
      .mockResolvedValueOnce(makeSession())
      .mockResolvedValue(filteredSession)

    const { wrapper } = await mountWithRouter('/', {
      global: {
        stubs: {
          FlightSearchBar: {
            emits: ['submit'],
            template: '<button class="submit-search" @click="$emit(\'submit\')">submit</button>',
          },
          SearchFilters: {
            emits: ['update:departureTimeRange'],
            template: '<button class="set-departure-filter" @click="$emit(\'update:departureTimeRange\', [0, 720])">filter</button>',
          },
          SearchResultCard: {
            props: ['result'],
            template: '<div class="result-card-stub">{{ result.id }}</div>',
          },
        },
      },
    })

    await wrapper.get('.submit-search').trigger('click')
    await flushPromises()

    await wrapper.get('.set-departure-filter').trigger('click')
    await vi.waitFor(() => {
      expect(mockGetSearchSession).toHaveBeenLastCalledWith('search-1', expect.objectContaining({
        direct: true,
        oneStop: false,
        twoPlusStop: false,
        departureTime: [0, 720],
        page: 1,
        pageSize: 100,
      }), expect.any(AbortSignal))
    })
    expect(document.title).toBe('Aveon · 1 flights from DUB to AMS')
  })

  it('hydrates the search form from URL params and keeps them in the address bar', async () => {
    const { wrapper, router } = await mountWithRouter(
      '/?origins=AMS&destinations=DUB&dates=2026-06-01,2026-06-03&adults=2&cabinClass=business',
      {
      global: {
        stubs: {
          FlightSearchBar: {
            props: [
              'originAirports',
              'destinationAirports',
              'selectedDepartureDates',
              'adults',
              'cabinClass',
              'searchCombinationCount',
            ],
            template: `
              <div>
                <span class="origins">{{ originAirports.map((airport) => airport.code).join(',') }}</span>
                <span class="destinations">{{ destinationAirports.map((airport) => airport.code).join(',') }}</span>
                <span class="dates">{{ selectedDepartureDates.join(',') }}</span>
                <span class="adults">{{ adults }}</span>
                <span class="cabin">{{ cabinClass }}</span>
                <span class="combinations">{{ searchCombinationCount }}</span>
              </div>
            `,
          },
          SearchFilters: true,
          SearchResultCard: true,
        },
      },
    })

    expect(wrapper.get('.origins').text()).toBe('AMS')
    expect(wrapper.get('.destinations').text()).toBe('DUB')
    expect(wrapper.get('.dates').text()).toBe('2026-06-01,2026-06-03')
    expect(wrapper.get('.adults').text()).toBe('2')
    expect(wrapper.get('.cabin').text()).toBe('business')
    expect(wrapper.get('.combinations').text()).toBe('2')
    expect(router.currentRoute.value.query.origins).toBe('AMS')
    expect(router.currentRoute.value.query.dates).toBe('2026-06-01,2026-06-03')
  })

  it('submits exact return dates for return searches and counts return combinations', async () => {
    mockSearchFlightsRequest.mockResolvedValue(makeSession())

    const { wrapper } = await mountWithRouter(
      '/?origins=AMS&destinations=DUB&dates=2026-06-01,2026-06-03&tripType=return&returnDates=2026-06-10,2026-06-12',
      {
        global: {
          stubs: {
            FlightSearchBar: {
              props: ['searchCombinationCount'],
              emits: ['submit'],
              template: `
                <div>
                  <span class="combination-prop">{{ searchCombinationCount }}</span>
                  <button class="submit-search" @click="$emit('submit')">submit</button>
                </div>
              `,
            },
            SearchFilters: true,
            SearchResultCard: true,
          },
        },
      },
    )

    expect(wrapper.get('.combination-prop').text()).toBe('8')

    await wrapper.get('.submit-search').trigger('click')
    await flushPromises()

    expect(mockSearchFlightsRequest).toHaveBeenCalledWith(expect.objectContaining({
      departureDates: ['2026-06-01', '2026-06-03'],
      returnDates: ['2026-06-10', '2026-06-12'],
    }), expect.any(AbortSignal))
  })

  it('refetches return searches with per-leg time filters', async () => {
    mockSearchFlightsRequest.mockResolvedValue(makeSession({
      response: {
        ...makeSession().response,
        results: [
          {
            id: 'return-1',
            isRoundTrip: true,
            totalDurationMinutes: 180,
            legs: [
              {
                id: 'return-outbound-leg',
                originAirport: 'AMS',
                destinationAirport: 'DUB',
                departureLocalTime: '2026-06-01T09:00:00',
                arrivalLocalTime: '2026-06-01T10:30:00',
                durationMinutes: 90,
                segments: [],
              },
              {
                id: 'return-return-leg',
                originAirport: 'DUB',
                destinationAirport: 'AMS',
                departureLocalTime: '2026-06-10T18:00:00',
                arrivalLocalTime: '2026-06-10T19:30:00',
                durationMinutes: 90,
                segments: [],
              },
            ],
            priceOptions: [
              {
                id: 'rp1',
                provider: 'FlightApi:KLM',
                totalPrice: { amount: 200, currency: 'EUR' },
                bookingLinks: [{ label: 'View fare', url: 'https://example.com/return-1' }],
              },
            ],
          },
        ],
        filters: {
          ...makeSession().response.filters,
          returnDepartureTimeMinutes: { min: 1080, max: 1080 },
          returnArrivalTimeMinutes: { min: 1170, max: 1170 },
        },
      },
    }))
    mockGetSearchSession.mockResolvedValue(makeSession())

    const { wrapper } = await mountWithRouter(
      '/?origins=AMS&destinations=DUB&dates=2026-06-01&tripType=return&returnDates=2026-06-10',
      {
        global: {
          stubs: {
            FlightSearchBar: {
              emits: ['submit'],
              template: '<button class="submit-search" @click="$emit(\'submit\')">submit</button>',
            },
            SearchFilters: {
              emits: ['update:returnDepartureTimeRange', 'update:returnArrivalTimeRange'],
              template: `
                <div>
                  <button class="set-return-departure-filter" @click="$emit('update:returnDepartureTimeRange', [1080, 1260])">return departure</button>
                  <button class="set-return-arrival-filter" @click="$emit('update:returnArrivalTimeRange', [1140, 1320])">return arrival</button>
                </div>
              `,
            },
            SearchResultCard: true,
          },
        },
      },
    )

    await wrapper.get('.submit-search').trigger('click')
    await flushPromises()

    await wrapper.get('.set-return-departure-filter').trigger('click')
    await wrapper.get('.set-return-arrival-filter').trigger('click')

    await vi.waitFor(() => {
      expect(mockGetSearchSession).toHaveBeenLastCalledWith('search-1', expect.objectContaining({
        direct: true,
        oneStop: false,
        twoPlusStop: false,
        returnDepartureTime: [1080, 1260],
        returnArrivalTime: [1140, 1320],
        page: 1,
        pageSize: 100,
      }), expect.any(AbortSignal))
    })

  })

  it('filters return combinations by a selected leg through the backend query', async () => {
    mockSearchFlightsRequest.mockResolvedValue(makeSession({
      response: {
        ...makeSession().response,
        results: [
          {
            id: 'return-1',
            isRoundTrip: true,
            totalDurationMinutes: 180,
            legs: [
              {
                id: 'return-outbound-leg',
                originAirport: 'AMS',
                destinationAirport: 'DUB',
                departureLocalTime: '2026-06-01T09:00:00',
                arrivalLocalTime: '2026-06-01T10:30:00',
                durationMinutes: 90,
                segments: [],
              },
              {
                id: 'return-return-leg',
                originAirport: 'DUB',
                destinationAirport: 'AMS',
                departureLocalTime: '2026-06-10T18:00:00',
                arrivalLocalTime: '2026-06-10T19:30:00',
                durationMinutes: 90,
                segments: [],
              },
            ],
            priceOptions: [
              {
                id: 'rp1',
                provider: 'FlightApi:KLM',
                totalPrice: { amount: 200, currency: 'EUR' },
                bookingLinks: [{ label: 'View fare', url: 'https://example.com/return-1' }],
              },
            ],
          },
        ],
      },
    }))
    mockGetSearchSession.mockResolvedValue(makeSession())

    const { wrapper } = await mountWithRouter(
      '/?origins=AMS&destinations=DUB&dates=2026-06-01&tripType=return&returnDates=2026-06-10',
      {
        global: {
          stubs: {
            FlightSearchBar: {
              emits: ['submit'],
              template: '<button class="submit-search" @click="$emit(\'submit\')">submit</button>',
            },
            SearchFilters: {
              emits: ['update:selectedProviders', 'update:maxDurationMinutes'],
              template: `
                <div>
                  <button class="set-return-provider-filter" @click="$emit('update:selectedProviders', ['FlightApi:Return Provider'])">provider</button>
                  <button class="set-return-duration-filter" @click="$emit('update:maxDurationMinutes', 60)">duration</button>
                </div>
              `,
            },
            SearchResultCard: {
              emits: ['filterLeg'],
              template: '<button class="filter-leg" @click="$emit(\'filterLeg\', { legId: \'return-outbound-leg\', legIndex: 0 })">filter leg</button>',
            },
          },
        },
      },
    )

    await wrapper.get('.submit-search').trigger('click')
    await flushPromises()

    await wrapper.get('.filter-leg').trigger('click')

    await vi.waitFor(() => {
      expect(mockGetSearchSession).toHaveBeenLastCalledWith('search-1', expect.objectContaining({
        direct: true,
        oneStop: false,
        twoPlusStop: false,
        outboundLegId: 'return-outbound-leg',
        page: 1,
        pageSize: 100,
      }), expect.any(AbortSignal))
    })

    await wrapper.get('.set-return-provider-filter').trigger('click')
    await wrapper.get('.set-return-duration-filter').trigger('click')

    await vi.waitFor(() => {
      expect(mockGetSearchSession).toHaveBeenLastCalledWith('search-1', expect.objectContaining({
        outboundLegId: 'return-outbound-leg',
        providers: ['FlightApi:Return Provider'],
        maxDuration: 60,
        page: 1,
        pageSize: 100,
      }), expect.any(AbortSignal))
    })
  })

  it('runs a search automatically when the route contains search params', async () => {
    mockSearchFlightsRequest.mockResolvedValue(makeSession())

    const { wrapper } = await mountWithRouter(
      '/?origins=DUB&destinations=AMS&dates=2026-05-15,2026-05-16,2026-05-17&adults=1',
      {
        global: {
          stubs: {
            FlightSearchBar: {
              props: ['isCollapsed'],
              template: '<div class="collapsed">{{ isCollapsed ? "yes" : "no" }}</div>',
            },
            SearchFilters: true,
            SearchResultCard: {
              props: ['result'],
              template: '<div class="result-card-stub">{{ result.id }}</div>',
            },
          },
        },
      },
    )

    expect(mockSearchFlightsRequest).toHaveBeenCalledWith(expect.objectContaining({
      originAirports: ['DUB'],
      destinationAirports: ['AMS'],
      departureDates: ['2026-05-15', '2026-05-16', '2026-05-17'],
      adults: 1,
      cabinClass: 'economy',
    }), expect.any(AbortSignal))
    expect(mockGetSearchSession).toHaveBeenCalledWith('search-1', expect.objectContaining({
      direct: true,
      oneStop: false,
      twoPlusStop: false,
      page: 1,
      pageSize: 100,
    }), expect.any(AbortSignal))
  })

  it('ignores an in-flight polling response after a newer search starts', async () => {
    let resolveOldPoll!: (session: SearchSessionResponse) => void
    const oldPoll = new Promise<SearchSessionResponse>((resolve) => {
      resolveOldPoll = resolve
    })
    const oldRunningSession = makeSession({
      searchId: 'search-old',
      status: 'running',
      completedCombinations: 1,
    })
    const newCompletedSession = makeSession({
      searchId: 'search-new',
      response: {
        ...makeSession().response,
        results: [{ ...makeSession().response.results[0], id: 'new-result' }],
      },
    })
    const staleCompletedSession = makeSession({
      searchId: 'search-old',
      response: {
        ...makeSession().response,
        results: [{ ...makeSession().response.results[0], id: 'stale-result' }],
      },
    })

    mockSearchFlightsRequest
      .mockResolvedValueOnce(oldRunningSession)
      .mockResolvedValueOnce(newCompletedSession)
    mockGetSearchSession.mockImplementation((searchId: string) => (
      searchId === 'search-old' ? oldPoll : Promise.resolve(newCompletedSession)
    ))

    const { wrapper } = await mountWithRouter('/', {
      global: {
        stubs: {
          FlightSearchBar: {
            emits: ['submit'],
            template: '<button class="submit-search" @click="$emit(\'submit\')">submit</button>',
          },
          SearchFilters: true,
          SearchResultCard: {
            props: ['result'],
            template: '<div class="result-card-stub">{{ result.id }}</div>',
          },
        },
      },
    })

    await wrapper.get('.submit-search').trigger('click')
    await vi.waitFor(() => expect(mockGetSearchSession).toHaveBeenCalledWith(
      'search-old',
      expect.any(Object),
      expect.any(AbortSignal),
    ))
    const oldSessionSignal = mockGetSearchSession.mock.calls.find((call) => call[0] === 'search-old')?.[2] as AbortSignal

    await wrapper.get('.submit-search').trigger('click')
    await vi.waitFor(() => expect(wrapper.text()).toContain('new-result'))
    expect(oldSessionSignal.aborted).toBe(true)

    resolveOldPoll(staleCompletedSession)
    await flushPromises()

    expect(wrapper.text()).toContain('new-result')
    expect(wrapper.text()).not.toContain('stale-result')
  })

  it('loads the next page automatically when the scroll sentinel is reached', async () => {
    const pageOneSession = makeSession({
      response: {
        ...makeSession().response,
        pagination: {
          page: 1,
          pageSize: 100,
          totalResults: 220,
          totalPages: 3,
        },
      },
    })

    mockSearchFlightsRequest.mockResolvedValue(pageOneSession)
    mockGetSearchSession
      .mockResolvedValueOnce(pageOneSession)
      .mockResolvedValueOnce(makeSession({
        response: {
          ...makeSession().response,
          pagination: {
            page: 2,
            pageSize: 100,
            totalResults: 220,
            totalPages: 3,
          },
          results: [
            {
              ...makeSession().response.results[0],
              id: 'page-2-result',
            },
          ],
        },
      }))

    const { wrapper, router } = await mountWithRouter('/?origins=DUB&destinations=AMS&dates=2026-05-15,2026-05-16,2026-05-17&adults=1', {
      global: {
        stubs: {
          FlightSearchBar: {
            template: '<div />',
          },
          SearchFilters: true,
          SearchResultCard: {
            props: ['result'],
            template: '<div class="result-card-stub">{{ result.id }}</div>',
          },
        },
      },
    })

    expect(mockGetSearchSession).toHaveBeenCalledWith('search-1', expect.objectContaining({
      page: 1,
      pageSize: 100,
    }), expect.any(AbortSignal))

    intersectionObserverCallbacks.at(-1)?.(
      [{ isIntersecting: true } as IntersectionObserverEntry],
      {} as IntersectionObserver,
    )

    await vi.waitFor(() => {
      expect(mockGetSearchSession).toHaveBeenLastCalledWith('search-1', expect.objectContaining({
        page: 2,
        pageSize: 100,
      }), expect.any(AbortSignal))
    })
    await vi.waitFor(() => expect(wrapper.text()).toContain('page-2-result'))
    expect(wrapper.text()).toContain('Showing 3 of 220')
    expect(router.currentRoute.value.query.page).toBeUndefined()
  })

})
