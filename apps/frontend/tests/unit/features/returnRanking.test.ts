import { describe, expect, it } from 'vitest'
import { rankReturnOptions } from '../../../src/features/flight-search/returnRanking'
import type { SearchResult } from '../../../src/features/flight-search/types'

const makeResult = (id: string, price: number, durationMinutes: number, stops = 0): SearchResult => ({
  id,
  isRoundTrip: true,
  totalDurationMinutes: durationMinutes,
  legs: [{
    id: `${id}-return`,
    originAirport: 'JFK',
    destinationAirport: 'DUB',
    departureLocalTime: '2026-08-20T18:00:00',
    arrivalLocalTime: '2026-08-21T06:00:00',
    durationMinutes,
    segments: Array.from({ length: stops + 1 }, (_, index) => ({
      marketingCarrierName: 'Test Air',
      marketingCarrierCode: 'TA',
      flightNumber: `${index + 1}`,
      originAirport: 'JFK',
      destinationAirport: 'DUB',
      departureLocalTime: '2026-08-20T18:00:00',
      arrivalLocalTime: '2026-08-21T06:00:00',
      durationMinutes: durationMinutes / (stops + 1),
    })),
  }],
  priceOptions: [{
    id: `${id}-fare`,
    provider: 'Test Air',
    totalPrice: { amount: price, currency: 'EUR' },
    bookingLinks: [{ label: 'Book', url: 'https://example.com' }],
  }],
})

describe('rankReturnOptions', () => {
  const fastest = makeResult('fastest', 190, 180)
  const cheapDetour = makeResult('cheap-detour', 180, 780, 1)
  const balanced = makeResult('balanced', 184, 230)

  it('keeps literal price and duration rankings available', () => {
    expect(rankReturnOptions([fastest, cheapDetour, balanced], 'cheapest')[0].id).toBe('cheap-detour')
    expect(rankReturnOptions([fastest, cheapDetour, balanced], 'fastest')[0].id).toBe('fastest')
  })

  it('does not promote a marginally cheaper but dramatically longer return as best value', () => {
    expect(rankReturnOptions([fastest, cheapDetour, balanced], 'best')[0].id).toBe('fastest')
  })
})
