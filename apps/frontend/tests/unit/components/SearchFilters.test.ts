import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import SearchFilters from '../../../src/components/flight-search/SearchFilters.vue'

const baseProps = {
  tripType: 'oneWay' as const,
  selectedOutboundLegId: null,
  providerFilters: [],
  airlineFilters: [],
  departureAirportFilters: [],
  arrivalAirportFilters: [],
  availableMaxDurationMinutes: 600,
  stopCounts: { direct: 12, oneStop: 7, twoPlusStop: 3, minimumAvailableStops: 0 },
  maxDurationMinutes: 600,
  includeDirectFlights: true,
  includeOneStopFlights: false,
  includeTwoPlusStopFlights: false,
  selectedProviders: [],
  selectedAirlines: [],
  selectedDepartureAirports: [],
  selectedArrivalAirports: [],
  departureTimeRange: [0, 1439] as [number, number],
  arrivalTimeRange: [0, 1439] as [number, number],
  returnDepartureTimeRange: [0, 1439] as [number, number],
  returnArrivalTimeRange: [0, 1439] as [number, number],
}

describe('SearchFilters stop counts', () => {
  it('shows and updates the available count beside every stop option', async () => {
    const wrapper = mount(SearchFilters, { props: baseProps })

    expect(wrapper.findAll('.stop-count').map(count => count.text())).toEqual(['12', '7', '3'])

    await wrapper.setProps({
      stopCounts: { direct: 0, oneStop: 4, twoPlusStop: 1, minimumAvailableStops: 1 },
    })

    expect(wrapper.findAll('.stop-count').map(count => count.text())).toEqual(['0', '4', '1'])
  })
})
