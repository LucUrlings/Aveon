import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import SearchResultsPanel from '../../../src/components/flight-search/SearchResultsPanel.vue'
import type { SearchResponse } from '../../../src/features/flight-search/types'

const response: SearchResponse = {
  results: [],
  metadata: {
    searchCombinationCount: 1,
    providerResultCount: 120,
    returnedResultCount: 100,
    returnedDirectFlightCount: 100,
    returnedOneStopFlightCount: 0,
    returnedTwoPlusStopFlightCount: 0,
  },
  filters: {
    providers: [],
    airlines: [],
    departureAirports: [],
    arrivalAirports: [],
    durationMinutes: { min: 0, max: 0 },
    departureTimeMinutes: { min: 0, max: 0 },
    arrivalTimeMinutes: { min: 0, max: 0 },
    returnDepartureTimeMinutes: { min: 0, max: 0 },
    returnArrivalTimeMinutes: { min: 0, max: 0 },
    stops: { direct: 120, oneStop: 0, twoPlusStop: 0 },
  },
  pagination: { page: 1, pageSize: 100, totalResults: 120, totalPages: 2 },
}

const mountPanel = (overrides: Record<string, unknown> = {}) => mount(SearchResultsPanel, {
  props: {
    tripType: 'oneWay',
    response,
    results: [],
    isPolling: false,
    isLoadingMore: false,
    selectedOutboundLegId: null,
    selectedReturnLegId: null,
    selectedOutboundSummaryResult: null,
    selectedRanking: 'best',
    rankingLabel: 'Recommended',
    expandedResultIds: [],
    currentPage: 1,
    hasMoreResults: true,
    paginationSummary: 'Showing 100 of 120',
    loadedStopCounts: { direct: 100, oneStop: 0, twoPlusStop: 0 },
    ...overrides,
  },
  global: {
    stubs: {
      SearchResultCard: true,
      SelectedOutboundSummary: true,
      ReturnRankingSelector: true,
    },
  },
})

beforeEach(() => {
  vi.stubGlobal('IntersectionObserver', class {
    observe() {}
    disconnect() {}
  })
})

describe('SearchResultsPanel', () => {
  it('warns that outbound prices do not include the return fare', () => {
    const wrapper = mountPanel({ tripType: 'return' })

    expect(wrapper.get('.outbound-price-notice').text()).toContain('Prices shown are for the outbound flight only')
    expect(wrapper.get('.outbound-price-notice').text()).toContain('total trip price')
  })

  it('emits loadMore from the manual pagination fallback', async () => {
    const wrapper = mountPanel()

    await wrapper.get('.load-more-button').trigger('click')

    expect(wrapper.emitted('loadMore')).toHaveLength(1)
  })

  it('recreates its scroll observer after polling and page loading finish', async () => {
    const observe = vi.fn()
    const disconnect = vi.fn()
    const constructor = vi.fn(() => ({ observe, disconnect }))
    vi.stubGlobal('IntersectionObserver', constructor)
    const wrapper = mountPanel({ isPolling: true })
    await flushPromises()
    const initialObserverCount = constructor.mock.calls.length

    await wrapper.setProps({ isPolling: false })
    await wrapper.setProps({ isLoadingMore: true })
    await wrapper.setProps({ isLoadingMore: false, currentPage: 2 })
    await flushPromises()

    expect(constructor.mock.calls.length).toBeGreaterThan(initialObserverCount)
    expect(disconnect).toHaveBeenCalled()
    expect(observe).toHaveBeenCalled()
  })

  it('hides pagination controls on the final page', () => {
    const wrapper = mountPanel({
      response: { ...response, pagination: { ...response.pagination, page: 2 } },
      currentPage: 2,
      hasMoreResults: false,
    })

    expect(wrapper.find('.load-more-button').exists()).toBe(false)
    expect(wrapper.find('.load-more-sentinel').exists()).toBe(false)
  })
})
