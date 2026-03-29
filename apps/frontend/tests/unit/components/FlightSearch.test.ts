import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import FlightSearch from '../../../src/components/FlightSearch.vue'
import type { SearchSessionResponse } from '../../../src/features/flight-search/types'

const {
  mockFetchAirportSuggestions,
  mockGetSearchSession,
  mockSearchFlightsRequest,
} = vi.hoisted(() => ({
  mockFetchAirportSuggestions: vi.fn(),
  mockGetSearchSession: vi.fn(),
  mockSearchFlightsRequest: vi.fn(),
}))

vi.mock('../../../src/features/flight-search/api', () => ({
  fetchAirportSuggestions: mockFetchAirportSuggestions,
  getSearchSession: mockGetSearchSession,
  searchFlightsRequest: mockSearchFlightsRequest,
}))

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
    results: [
      {
        id: 'morning',
        isRoundTrip: false,
        totalDurationMinutes: 90,
        legs: [
          {
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
            deepLink: 'https://example.com/1',
          },
        ],
      },
      {
        id: 'evening',
        isRoundTrip: false,
        totalDurationMinutes: 90,
        legs: [
          {
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
            deepLink: 'https://example.com/2',
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
  mockFetchAirportSuggestions.mockResolvedValue([])
  document.title = ''
})

describe('FlightSearch', () => {
  it('submits selectedDates to the backend and passes combination count to the search bar', async () => {
    mockSearchFlightsRequest.mockResolvedValue(makeSession())

    const wrapper = mount(FlightSearch, {
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
      selectedDates: ['2026-05-15', '2026-05-16', '2026-05-17'],
    }))
  })

  it('filters by local departure time and updates the page title from filtered results', async () => {
    mockSearchFlightsRequest.mockResolvedValue(makeSession())

    const wrapper = mount(FlightSearch, {
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

    expect(wrapper.findAll('.result-card-stub')).toHaveLength(2)

    await wrapper.get('.set-departure-filter').trigger('click')
    await flushPromises()

    expect(wrapper.findAll('.result-card-stub').map((node) => node.text())).toEqual(['morning'])
    expect(document.title).toBe('Aveon · 1 flights from DUB to AMS')
  })
})
