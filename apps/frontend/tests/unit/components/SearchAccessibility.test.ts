import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import SearchFilters from '../../../src/components/flight-search/SearchFilters.vue'
import SearchProgress from '../../../src/components/flight-search/SearchProgress.vue'

const mountFilters = () => mount(SearchFilters, {
  props: {
    tripType: 'oneWay', providerFilters: ['Provider'], airlineFilters: ['Airline'],
    departureAirportFilters: ['DUB'], arrivalAirportFilters: ['AMS'], availableMaxDurationMinutes: 600,
    maxDurationMinutes: 600, includeDirectFlights: true, includeOneStopFlights: false,
    includeTwoPlusStopFlights: false, selectedProviders: ['Provider'], selectedAirlines: ['Airline'],
    selectedDepartureAirports: ['DUB'], selectedArrivalAirports: ['AMS'],
    departureTimeRange: [0, 1439], arrivalTimeRange: [0, 1439],
    returnDepartureTimeRange: [0, 1439], returnArrivalTimeRange: [0, 1439],
    'onUpdate:maxDurationMinutes': () => {}, 'onUpdate:includeDirectFlights': () => {},
    'onUpdate:includeOneStopFlights': () => {}, 'onUpdate:includeTwoPlusStopFlights': () => {},
    'onUpdate:selectedProviders': () => {}, 'onUpdate:selectedAirlines': () => {},
    'onUpdate:selectedDepartureAirports': () => {}, 'onUpdate:selectedArrivalAirports': () => {},
    'onUpdate:departureTimeRange': () => {}, 'onUpdate:arrivalTimeRange': () => {},
    'onUpdate:returnDepartureTimeRange': () => {}, 'onUpdate:returnArrivalTimeRange': () => {},
  },
})

describe('search accessibility', () => {
  it('removes collapsed filter controls from keyboard navigation', async () => {
    const wrapper = mountFilters()
    const sourcePanel = wrapper.get('#filter-sources')
    expect(sourcePanel.attributes('style')).toContain('display: none')

    await wrapper.get('button[aria-controls="filter-sources"]').trigger('click')

    expect(sourcePanel.attributes('style')).not.toContain('display: none')
    expect(wrapper.get('button[aria-controls="filter-sources"]').attributes('aria-expanded')).toBe('true')
  })

  it('exposes determinate search progress to assistive technology', () => {
    const wrapper = mount(SearchProgress, {
      props: {
        session: {
          searchId: 'search-1', status: 'running', totalCombinations: 10,
          completedCombinations: 4, failedCombinations: 0,
          response: {
            results: [],
            metadata: { searchCombinationCount: 10, providerResultCount: 0, returnedResultCount: 0, returnedDirectFlightCount: 0, returnedOneStopFlightCount: 0, returnedTwoPlusStopFlightCount: 0 },
            filters: { providers: [], airlines: [], departureAirports: [], arrivalAirports: [], durationMinutes: { min: 0, max: 0 }, departureTimeMinutes: { min: 0, max: 0 }, arrivalTimeMinutes: { min: 0, max: 0 }, returnDepartureTimeMinutes: { min: 0, max: 0 }, returnArrivalTimeMinutes: { min: 0, max: 0 }, stops: { direct: 0, oneStop: 0, twoPlusStop: 0 } },
            pagination: { page: 1, pageSize: 100, totalResults: 0, totalPages: 0 },
          },
          errorMessage: null,
        },
      },
    })

    const progressbar = wrapper.get('[role="progressbar"]')
    expect(wrapper.get('.progress-spinner').attributes('aria-hidden')).toBe('true')
    expect(progressbar.attributes('aria-valuenow')).toBe('4')
    expect(progressbar.attributes('aria-valuemax')).toBe('10')
    expect(progressbar.attributes('aria-valuetext')).toContain('4 of 10')
  })
})
