import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import SearchResultCard from '../../../src/components/flight-search/SearchResultCard.vue'
import type { SearchResult } from '../../../src/features/flight-search/types'

const clipboardWriteText = vi.fn()

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
      bookingLinks: [{ label: 'View fare', url: 'https://example.com/direct' }],
    },
  ],
}

const multiLegResult: SearchResult = {
  ...directResult,
  id: 'multi-1',
  isRoundTrip: true,
  totalDurationMinutes: 240,
  legs: [
    {
      originAirport: 'AMS',
      destinationAirport: 'CDG',
      departureLocalTime: '2026-05-15T08:00:00',
      arrivalLocalTime: '2026-05-15T09:00:00',
      durationMinutes: 60,
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
      ],
    },
    {
      originAirport: 'CDG',
      destinationAirport: 'AMS',
      departureLocalTime: '2026-05-18T10:30:00',
      arrivalLocalTime: '2026-05-18T12:00:00',
      durationMinutes: 90,
      segments: [
        {
          marketingCarrierName: 'Air France',
          marketingCarrierCode: 'AF',
          flightNumber: '200',
          originAirport: 'CDG',
          destinationAirport: 'AMS',
          departureLocalTime: '2026-05-18T10:30:00',
          arrivalLocalTime: '2026-05-18T12:00:00',
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
      bookingLinks: [{ label: 'View fare', url: 'https://example.com/main' }],
    },
    {
      id: 'price-2',
      provider: 'FlightApi:KLM',
      totalPrice: { amount: 150, currency: 'EUR' },
      bookingLinks: [
        { label: 'Book outbound', url: 'https://example.com/outbound' },
        { label: 'Book return', url: 'https://example.com/return' },
      ],
    },
  ],
}

const actualReturnResult: SearchResult = {
  ...multiLegResult,
  id: 'return-1',
  priceOptions: [
    {
      id: 'price-actual',
      provider: 'FlightApi:Air France',
      totalPrice: { amount: 140, currency: 'EUR' },
      bookingLinks: [{ label: 'View fare', url: 'https://example.com/main' }],
    },
  ],
}

const syntheticReturnResult: SearchResult = {
  ...multiLegResult,
  id: 'synthetic-return-1',
  priceOptions: [
    {
      id: 'price-synthetic',
      provider: 'FlightApi:Combined one-way (KLM + Air France)',
      totalPrice: { amount: 150, currency: 'EUR' },
      bookingLinks: [
        { label: 'Book outbound', url: 'https://example.com/outbound' },
        { label: 'Book return', url: 'https://example.com/return' },
      ],
    },
    {
      id: 'price-other',
      provider: 'FlightApi:Combined one-way (KLM + KLM)',
      totalPrice: { amount: 170, currency: 'EUR' },
      bookingLinks: [
        { label: 'Book outbound', url: 'https://example.com/outbound-2' },
        { label: 'Book return', url: 'https://example.com/return-2' },
      ],
    },
  ],
}

describe('SearchResultCard', () => {
  beforeEach(() => {
    clipboardWriteText.mockReset()
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: clipboardWriteText,
      },
    })
  })

  it('renders a direct one-way flight using local-time formatting', () => {
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
    expect(wrapper.text()).toContain('View fare')
  })

  it('renders an actual return fare using the round-trip layout', () => {
    const wrapper = mount(SearchResultCard, {
      props: {
        result: actualReturnResult,
        expanded: false,
      },
    })

    expect(wrapper.text()).toContain('Round trip')
    expect(wrapper.text()).toContain('Outbound')
    expect(wrapper.text()).toContain('Return')
    expect(wrapper.text()).toContain('Single round-trip booking')
  })

  it('renders synthetic return booking links and emits fare toggle for multi-option results', async () => {
    const wrapper = mount(SearchResultCard, {
      props: {
        result: syntheticReturnResult,
        expanded: false,
      },
    })

    expect(wrapper.text()).toContain('Separate bookings')
    expect(wrapper.text()).toContain('Outbound')
    expect(wrapper.text()).toContain('Return')

    await wrapper.get('.expand-button').trigger('click')
    await wrapper.setProps({ expanded: true })

    expect(wrapper.emitted('toggleExpanded')).toEqual([['synthetic-return-1']])
    expect(wrapper.text()).toContain('Book outbound')
    expect(wrapper.text()).toContain('Book return')
  })

  it('copies a single fare into a shareable message from the corner button', async () => {
    const wrapper = mount(SearchResultCard, {
      props: {
        result: syntheticReturnResult,
        expanded: false,
      },
    })

    await wrapper.get('.copy-fare-button').trigger('click')

    expect(clipboardWriteText).toHaveBeenCalledWith(expect.stringContaining('1. Round trip | KLM, Air France'))
    expect(clipboardWriteText).toHaveBeenCalledWith(expect.stringContaining('Outbound: AMS -> CDG'))
    expect(clipboardWriteText).toHaveBeenCalledWith(expect.stringContaining('Book outbound: https://example.com/outbound'))
  })
})
