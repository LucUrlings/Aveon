import { describe, expect, it, vi, beforeEach } from 'vitest'
import { useSearchSession } from '../../../src/features/flight-search/useSearchSession'
import type { SearchResult, SearchSessionResponse } from '../../../src/features/flight-search/types'

const { mockGetSearchSession, mockSearchFlightsRequest } = vi.hoisted(() => ({
  mockGetSearchSession: vi.fn(),
  mockSearchFlightsRequest: vi.fn(),
}))

vi.mock('../../../src/features/flight-search/api', () => ({
  getSearchSession: mockGetSearchSession,
  searchFlightsRequest: mockSearchFlightsRequest,
}))

const makeResult = (id: string, amount = 100): SearchResult => ({
  id,
  isRoundTrip: false,
  totalDurationMinutes: 90,
  legs: [{
    id: `${id}-leg`,
    originAirport: 'DUB',
    destinationAirport: 'AMS',
    departureLocalTime: '2026-08-07T09:00:00',
    arrivalLocalTime: '2026-08-07T10:30:00',
    durationMinutes: 90,
    segments: [],
  }],
  priceOptions: [{
    id: `${id}-fare`,
    provider: 'Test Air',
    totalPrice: { amount, currency: 'EUR' },
    bookingLinks: [],
  }],
})

const makeSession = (
  page: number,
  results: SearchResult[],
  totalPages = 2,
): SearchSessionResponse => ({
  searchId: 'search-1',
  status: 'completed',
  totalCombinations: 1,
  completedCombinations: 1,
  failedCombinations: 0,
  response: {
    results,
    metadata: {
      searchCombinationCount: 1,
      providerResultCount: results.length,
      returnedResultCount: results.length,
      returnedDirectFlightCount: results.length,
      returnedOneStopFlightCount: 0,
      returnedTwoPlusStopFlightCount: 0,
    },
    filters: {
      providers: [],
      airlines: [],
      departureAirports: [],
      arrivalAirports: [],
      durationMinutes: { min: 90, max: 90 },
      departureTimeMinutes: { min: 540, max: 540 },
      arrivalTimeMinutes: { min: 630, max: 630 },
      returnDepartureTimeMinutes: { min: 0, max: 0 },
      returnArrivalTimeMinutes: { min: 0, max: 0 },
      stops: { direct: results.length, oneStop: 0, twoPlusStop: 0 },
    },
    pagination: { page, pageSize: 2, totalResults: 3, totalPages },
  },
  errorMessage: null,
})

const createSessionState = (validateRequest = () => null) => useSearchSession({
  buildQuery: () => ({ direct: true, pageSize: 2 }),
  buildRequest: () => ({
    originAirports: ['DUB'],
    destinationAirports: ['AMS'],
    departureDates: ['2026-08-07'],
    returnDates: [],
    adults: 1,
    cabinClass: 'economy',
  }),
  getSearchKey: () => 'request-key',
  validateRequest,
  isReady: () => true,
  onSearchReset: vi.fn(),
  onSearchAccepted: vi.fn(),
})

beforeEach(() => {
  mockGetSearchSession.mockReset()
  mockSearchFlightsRequest.mockReset()
})

describe('useSearchSession', () => {
  it('loads subsequent pages and merges results without duplicating matching IDs', async () => {
    const initial = makeSession(1, [makeResult('first'), makeResult('shared', 120)])
    const second = makeSession(2, [makeResult('shared', 95), makeResult('third')])
    mockSearchFlightsRequest.mockResolvedValue(initial)
    mockGetSearchSession.mockResolvedValueOnce(initial).mockResolvedValueOnce(second)
    const state = createSessionState()

    await state.search()
    await state.loadNextPage()

    expect(mockGetSearchSession).toHaveBeenLastCalledWith(
      'search-1',
      expect.objectContaining({ page: 2, pageSize: 2 }),
      expect.any(AbortSignal),
    )
    expect(state.loadedResults.value.map((result) => result.id)).toEqual(['first', 'shared', 'third'])
    expect(state.loadedResults.value.find((result) => result.id === 'shared')?.priceOptions[0].totalPrice.amount).toBe(95)
    expect(state.currentPage.value).toBe(2)
    expect(state.hasMoreResults.value).toBe(false)
  })

  it('does not start a request when validation fails', async () => {
    const state = createSessionState(() => 'Choose a valid return date.')

    await state.search()

    expect(state.error.value).toBe('Choose a valid return date.')
    expect(mockSearchFlightsRequest).not.toHaveBeenCalled()
  })

  it('aborts an active search when disposed', () => {
    let signal: AbortSignal | undefined
    mockSearchFlightsRequest.mockImplementation((_request, requestSignal: AbortSignal) => {
      signal = requestSignal
      return new Promise(() => {})
    })
    const state = createSessionState()

    void state.search()
    state.dispose()

    expect(signal?.aborted).toBe(true)
  })
})
