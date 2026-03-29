import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import SearchResultCard from '../../../src/components/flight-search/SearchResultCard.vue'
import type { SearchResult } from '../../../src/features/flight-search/types'

const directResult: SearchResult = {
  id: 'direct-1',
  isRoundTrip: false,
  totalDurationMinutes: 95,
  legs: [
    {
      originAirport: 'AMS',
      destinationAirport: 'DUB',
      departureLocalTime: '2026-05-15T09:45:00',
      arrivalLocalTime: '2026-05-15T11:20:00',
      durationMinutes: 95,
      segments: [
        {
          marketingCarrierName: 'KLM',
          marketingCarrierCode: 'KL',
          flightNumber: '1137',
          originAirport: 'AMS',
          destinationAirport: 'DUB',
          departureLocalTime: '2026-05-15T09:45:00',
          arrivalLocalTime: '2026-05-15T11:20:00',
          durationMinutes: 95,
        },
      ],
    },
  ],
  priceOptions: [
    {
      id: 'price-1',
      provider: 'FlightApi:KLM',
      totalPrice: { amount: 120, currency: 'EUR' },
      deepLink: 'https://example.com/direct',
    },
  ],
}

const multiLegResult: SearchResult = {
  ...directResult,
  id: 'multi-1',
  totalDurationMinutes: 240,
  legs: [
    {
      originAirport: 'AMS',
      destinationAirport: 'DUB',
      departureLocalTime: '2026-05-15T08:00:00',
      arrivalLocalTime: '2026-05-15T12:00:00',
      durationMinutes: 240,
      segments: [
        {
          marketingCarrierName: 'KLM',
          marketingCarrierCode: 'KL',
          flightNumber: '100',
          originAirport: 'AMS',
          destinationAirport: 'CDG',
          departureLocalTime: '2026-05-15T08:00:00',
          arrivalLocalTime: '2026-05-15T09:00:00',
          durationMinutes: 60,
        },
        {
          marketingCarrierName: 'Air France',
          marketingCarrierCode: 'AF',
          flightNumber: '200',
          originAirport: 'CDG',
          destinationAirport: 'DUB',
          departureLocalTime: '2026-05-15T10:30:00',
          arrivalLocalTime: '2026-05-15T12:00:00',
          durationMinutes: 90,
        },
      ],
    },
  ],
  priceOptions: [
    {
      id: 'price-1',
      provider: 'FlightApi:Air France',
      totalPrice: { amount: 140, currency: 'EUR' },
      deepLink: 'https://example.com/main',
    },
    {
      id: 'price-2',
      provider: 'FlightApi:KLM',
      totalPrice: { amount: 150, currency: 'EUR' },
      deepLink: 'https://example.com/other',
    },
  ],
}

describe('SearchResultCard', () => {
  it('renders a direct flight using local-time formatting', () => {
    const wrapper = mount(SearchResultCard, {
      props: {
        result: directResult,
        expanded: false,
      },
    })

    expect(wrapper.text()).toContain('Fr 15 May 09:45')
    expect(wrapper.text()).toContain('Fr 15 May 11:20')
    expect(wrapper.text()).toContain('KLM')
    expect(wrapper.text()).toContain('EUR')
  })

  it('renders segment details and emits fare toggle for multi-option results', async () => {
    const wrapper = mount(SearchResultCard, {
      props: {
        result: multiLegResult,
        expanded: false,
      },
    })

    expect(wrapper.text()).toContain('KLM, Air France')
    expect(wrapper.text()).toContain('AMS → CDG')
    expect(wrapper.text()).toContain('CDG → DUB')

    await wrapper.get('.expand-button').trigger('click')

    expect(wrapper.emitted('toggleExpanded')).toEqual([['multi-1']])
  })
})
