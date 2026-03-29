import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import FlightSearchBar from '../../../src/components/flight-search/FlightSearchBar.vue'

describe('FlightSearchBar', () => {
  it('emits submit and airport actions', async () => {
    const wrapper = mount(FlightSearchBar, {
      props: {
        responseExists: true,
        isCollapsed: false,
        compactSummary: 'AMS to DUB on 2026-05-15',
        searchCombinationCount: 3,
        maxDepartureRangeDays: 10,
        loading: false,
        originSuggestions: [{ code: 'AMS', name: 'Amsterdam', displayLabel: 'Amsterdam (AMS)' }],
        destinationSuggestions: [{ code: 'DUB', name: 'Dublin', displayLabel: 'Dublin (DUB)' }],
        cabinOptions: [{ label: 'Economy', value: 'economy' }],
        originInput: 'AMS',
        destinationInput: 'DUB',
        originAirports: [{ code: 'AMS', name: 'Amsterdam', displayLabel: 'Amsterdam (AMS)' }],
        destinationAirports: [{ code: 'DUB', name: 'Dublin', displayLabel: 'Dublin (DUB)' }],
        departureDateFrom: '2026-05-15',
        departureDateTo: '2026-05-17',
        selectedDepartureDates: ['2026-05-15', '2026-05-16', '2026-05-17'],
        adults: 1,
        cabinClass: 'economy',
        'onUpdate:originInput': () => {},
        'onUpdate:destinationInput': () => {},
        'onUpdate:originAirports': () => {},
        'onUpdate:destinationAirports': () => {},
        'onUpdate:departureDateFrom': () => {},
        'onUpdate:departureDateTo': () => {},
        'onUpdate:selectedDepartureDates': () => {},
        'onUpdate:adults': () => {},
        'onUpdate:cabinClass': () => {},
      },
      global: {
        stubs: {
          DateRangePicker: {
            template: '<div class="date-picker-stub" />',
          },
        },
      },
    })

    await wrapper.get('form').trigger('submit')
    await wrapper.get('.collapse-toggle').trigger('click')
    await wrapper.get('input[placeholder="Add airport or city"]').trigger('keydown.enter')
    await wrapper.findAll('.suggestion-button')[0].trigger('click')

    expect(wrapper.emitted('submit')).toBeTruthy()
    expect(wrapper.emitted('toggleCollapse')).toBeTruthy()
    expect(wrapper.emitted('confirmOriginInput')).toBeTruthy()
    expect(wrapper.emitted('addOriginAirport')).toBeTruthy()
  })
})
