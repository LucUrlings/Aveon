import { afterEach, describe, expect, it, vi } from 'vitest'

const fetchMock = vi.fn()
vi.stubGlobal('fetch', fetchMock)

afterEach(() => {
  fetchMock.mockReset()
})

describe('flight search api', () => {
  it('normalizes airport suggestions and missing search fields', async () => {
    fetchMock
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          airports: [
            { code: 'DUB', displayLabel: 'Dublin Airport' },
          ],
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          searchId: 'search-1',
          status: 'completed',
          totalCombinations: 2,
          completedCombinations: 2,
          failedCombinations: 0,
          response: {
            results: [
              {
                id: 'result-1',
                legs: [
                  {
                    originAirport: 'AMS',
                    destinationAirport: 'DUB',
                    departureLocalTime: '2026-05-15T08:00:00',
                    arrivalLocalTime: '2026-05-15T09:30:00',
                    durationMinutes: 90,
                    segments: [
                      {
                        marketingCarrierName: null,
                        marketingCarrierCode: null,
                        flightNumber: null,
                        originAirport: null,
                        destinationAirport: null,
                        departureLocalTime: null,
                        arrivalLocalTime: null,
                        durationMinutes: null,
                      },
                    ],
                  },
                ],
                totalDurationMinutes: 90,
                priceOptions: [
                  {
                    id: 'price-1',
                    provider: 'FlightApi:KLM',
                    totalPrice: { amount: 123.45, currency: 'EUR' },
                    bookingLinks: [{ label: 'View fare', url: 'https://example.com/fare', price: { amount: 123.45, currency: 'EUR' } }],
                  },
                ],
              },
            ],
            filters: {
              providers: [{ value: 'FlightApi:KLM', count: 1 }],
              airlines: [{ value: 'KLM', count: 1 }],
              departureAirports: [{ value: 'AMS', count: 1 }],
              arrivalAirports: [{ value: 'DUB', count: 1 }],
              durationMinutes: { min: 90, max: 90 },
              departureTimeMinutes: { min: 480, max: 480 },
              arrivalTimeMinutes: { min: 570, max: 570 },
              stops: { direct: 1, oneStop: 0, twoPlusStop: 0 },
            },
            pagination: {
              page: 1,
              pageSize: 100,
              totalResults: 1,
              totalPages: 1,
            },
          },
        }),
      })

    const { fetchAirportSuggestions, searchFlightsRequest } = await import('../../../src/features/flight-search/api')

    const airports = await fetchAirportSuggestions('dub')
    const session = await searchFlightsRequest({
      originAirports: ['AMS'],
      destinationAirports: ['DUB'],
      selectedDates: ['2026-05-15'],
      returnDateFrom: null,
      returnDateTo: null,
      adults: 1,
      cabinClass: 'economy',
    })

    expect(airports).toEqual([
      { code: 'DUB', name: null, displayLabel: 'Dublin Airport' },
    ])
    expect(session.response.results[0].legs[0].segments[0]).toEqual({
      marketingCarrierName: 'Unknown airline',
      marketingCarrierCode: '',
      flightNumber: '',
      originAirport: '',
      destinationAirport: '',
      departureLocalTime: '',
      arrivalLocalTime: '',
      durationMinutes: 0,
    })
    expect(session.response.results[0].priceOptions[0].bookingLinks).toEqual([
      { label: 'View fare', url: 'https://example.com/fare', price: { amount: 123.45, currency: 'EUR' } },
    ])
    expect(session.response.filters.providers).toEqual([{ value: 'FlightApi:KLM', count: 1 }])
    expect(session.response.pagination.totalResults).toBe(1)
  })

  it('throws the backend message for non-ok responses', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 400,
      text: async () => 'Bad request from backend',
    })

    const { getSearchSession } = await import('../../../src/features/flight-search/api')

    await expect(getSearchSession('search-1', { direct: true, providers: ['FlightApi:KLM'] })).rejects.toThrow('Bad request from backend')
  })

  it('serializes pagination and filter query params for session reads', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({
        searchId: 'search-1',
        status: 'completed',
        totalCombinations: 1,
        completedCombinations: 1,
        failedCombinations: 0,
        response: {
          results: [],
          filters: {
            providers: [],
            airlines: [],
            departureAirports: [],
            arrivalAirports: [],
            durationMinutes: { min: 0, max: 0 },
            departureTimeMinutes: { min: 0, max: 0 },
            arrivalTimeMinutes: { min: 0, max: 0 },
            stops: { direct: 0, oneStop: 0, twoPlusStop: 0 },
          },
          pagination: {
            page: 2,
            pageSize: 100,
            totalResults: 150,
            totalPages: 2,
          },
          metadata: {
            searchCombinationCount: 1,
            providerResultCount: 0,
            returnedResultCount: 0,
            returnedDirectFlightCount: 0,
            returnedOneStopFlightCount: 0,
            returnedTwoPlusStopFlightCount: 0,
          },
        },
      }),
    })

    const { getSearchSession } = await import('../../../src/features/flight-search/api')

    await getSearchSession('search-1', {
      direct: true,
      oneStop: false,
      providers: ['FlightApi:KLM'],
      departureTime: [0, 720],
      page: 2,
      pageSize: 100,
    })

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/search/search-1?'),
    )

    const requestUrl = new URL(fetchMock.mock.calls[0][0] as string)
    expect(requestUrl.searchParams.get('direct')).toBe('true')
    expect(requestUrl.searchParams.get('oneStop')).toBe('false')
    expect(requestUrl.searchParams.get('providers')).toBe('FlightApi:KLM')
    expect(requestUrl.searchParams.get('departureTime')).toBe('0-720')
    expect(requestUrl.searchParams.get('page')).toBe('2')
    expect(requestUrl.searchParams.get('pageSize')).toBe('100')
  })
})
