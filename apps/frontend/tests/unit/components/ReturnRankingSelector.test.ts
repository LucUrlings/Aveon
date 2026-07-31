import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ReturnRankingSelector from '../../../src/components/flight-search/ReturnRankingSelector.vue'
import type { SearchResult } from '../../../src/features/flight-search/types'

const makeResult = (id: string, price: number, durationMinutes: number): SearchResult => ({
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
    segments: [{
      marketingCarrierName: 'Test Air',
      marketingCarrierCode: 'TA',
      flightNumber: '1',
      originAirport: 'JFK',
      destinationAirport: 'DUB',
      departureLocalTime: '2026-08-20T18:00:00',
      arrivalLocalTime: '2026-08-21T06:00:00',
      durationMinutes,
    }],
  }],
  priceOptions: [{
    id: `${id}-fare`,
    provider: 'Test Air',
    totalPrice: { amount: price, currency: 'EUR' },
    bookingLinks: [{ label: 'Book', url: 'https://example.com' }],
  }],
})

describe('ReturnRankingSelector', () => {
  it('compares ranking choices with fare and duration, then emits the choice', async () => {
    const wrapper = mount(ReturnRankingSelector, {
      props: {
        results: [makeResult('fast', 190, 180), makeResult('cheap', 180, 420)],
        selectedRanking: 'best',
      },
    })

    expect(wrapper.text()).toContain('Recommended')
    expect(wrapper.text()).toContain('Cheapest')
    expect(wrapper.text()).toContain('Fastest')
    expect(wrapper.text()).toContain('180.00')
    expect(wrapper.text()).toContain('3h 0m return')

    await wrapper.findAll('.return-ranking-options button')[1].trigger('click')
    expect(wrapper.emitted('select')).toEqual([['cheapest']])

    await wrapper.findAll('.return-ranking-options button')[0].trigger('keydown', { key: 'ArrowRight' })
    expect(wrapper.emitted('select')?.at(-1)).toEqual(['cheapest'])
    expect(wrapper.findAll('[role="radio"]')[0].attributes('tabindex')).toBe('0')
    expect(wrapper.findAll('[role="radio"]')[1].attributes('tabindex')).toBe('-1')
  })
})
