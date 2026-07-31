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
        tripType: 'oneWay',
        departureDateFrom: '2026-05-15',
        departureDateTo: '2026-05-17',
        selectedDepartureDates: ['2026-05-15', '2026-05-16', '2026-05-17'],
        returnDateFrom: null,
        returnDateTo: null,
        selectedReturnDates: [],
        adults: 1,
        cabinClass: 'economy',
        'onUpdate:originInput': () => {},
        'onUpdate:destinationInput': () => {},
        'onUpdate:originAirports': () => {},
        'onUpdate:destinationAirports': () => {},
        'onUpdate:tripType': () => {},
        'onUpdate:departureDateFrom': () => {},
        'onUpdate:departureDateTo': () => {},
        'onUpdate:selectedDepartureDates': () => {},
        'onUpdate:returnDateFrom': () => {},
        'onUpdate:returnDateTo': () => {},
        'onUpdate:selectedReturnDates': () => {},
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
    await wrapper.get('.swap-locations-button').trigger('click')
    await wrapper.get('input[placeholder="Add airport or city"]').trigger('keydown', { key: 'Enter' })
    await wrapper.findAll('.suggestion-button')[0].trigger('click')

    expect(wrapper.emitted('submit')).toBeTruthy()
    expect(wrapper.emitted('toggleCollapse')).toBeTruthy()
    expect(wrapper.emitted('swapLocations')).toBeTruthy()
    expect(wrapper.emitted('confirmOriginInput')).toBeTruthy()
    expect(wrapper.emitted('addOriginAirport')).toBeTruthy()
    expect(wrapper.get('input[aria-label="Add an origin airport or city"]').attributes('aria-controls')).toBe('origin-suggestions')
    expect(wrapper.get('input[aria-label="Add an origin airport or city"]').attributes('role')).toBe('combobox')
    expect(wrapper.get('.airport-chip').attributes('aria-label')).toContain('Remove Amsterdam')
    expect(wrapper.get('.combination-count').attributes('aria-live')).toBe('polite')
  })

  it('supports keyboard navigation and selection in airport suggestions', async () => {
    const wrapper = mount(FlightSearchBar, {
      props: {
        responseExists: false, isCollapsed: false, compactSummary: '', searchCombinationCount: 1,
        maxDepartureRangeDays: 10, loading: false,
        originSuggestions: [
          { code: 'AMS', name: 'Amsterdam', displayLabel: 'Amsterdam (AMS)' },
          { code: 'EIN', name: 'Eindhoven', displayLabel: 'Eindhoven (EIN)' },
        ],
        destinationSuggestions: [], cabinOptions: [{ label: 'Economy', value: 'economy' }],
        originInput: 'A', destinationInput: '', originAirports: [], destinationAirports: [],
        tripType: 'oneWay', departureDateFrom: '2026-05-15', departureDateTo: '2026-05-15',
        selectedDepartureDates: ['2026-05-15'], returnDateFrom: null, returnDateTo: null,
        selectedReturnDates: [], adults: 1, cabinClass: 'economy',
        'onUpdate:originInput': () => {}, 'onUpdate:destinationInput': () => {},
        'onUpdate:originAirports': () => {}, 'onUpdate:destinationAirports': () => {},
        'onUpdate:tripType': () => {}, 'onUpdate:departureDateFrom': () => {},
        'onUpdate:departureDateTo': () => {}, 'onUpdate:selectedDepartureDates': () => {},
        'onUpdate:returnDateFrom': () => {}, 'onUpdate:returnDateTo': () => {},
        'onUpdate:selectedReturnDates': () => {}, 'onUpdate:adults': () => {},
        'onUpdate:cabinClass': () => {},
      },
      global: { stubs: { DateRangePicker: { template: '<div />' } } },
    })
    const input = wrapper.get('input[aria-label="Add an origin airport or city"]')

    await input.trigger('keydown', { key: 'ArrowDown' })
    expect(input.attributes('aria-activedescendant')).toBe('origin-suggestion-AMS')
    await input.trigger('keydown', { key: 'ArrowDown' })
    expect(input.attributes('aria-activedescendant')).toBe('origin-suggestion-EIN')
    await input.trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('addOriginAirport')?.at(-1)?.[0]).toMatchObject({ code: 'EIN' })
  })

  it('shows return date inputs for return trips', () => {
    const wrapper = mount(FlightSearchBar, {
      props: {
        responseExists: false,
        isCollapsed: false,
        compactSummary: 'AMS to DUB',
        searchCombinationCount: 3,
        maxDepartureRangeDays: 10,
        loading: false,
        originSuggestions: [],
        destinationSuggestions: [],
        cabinOptions: [{ label: 'Economy', value: 'economy' }],
        originInput: '',
        destinationInput: '',
        originAirports: [],
        destinationAirports: [],
        tripType: 'return',
        departureDateFrom: '2026-05-15',
        departureDateTo: '2026-05-17',
        selectedDepartureDates: ['2026-05-15', '2026-05-16', '2026-05-17'],
        returnDateFrom: '2026-05-20',
        returnDateTo: '2026-05-21',
        selectedReturnDates: ['2026-05-20', '2026-05-21'],
        adults: 1,
        cabinClass: 'economy',
        'onUpdate:originInput': () => {},
        'onUpdate:destinationInput': () => {},
        'onUpdate:originAirports': () => {},
        'onUpdate:destinationAirports': () => {},
        'onUpdate:tripType': () => {},
        'onUpdate:departureDateFrom': () => {},
        'onUpdate:departureDateTo': () => {},
        'onUpdate:selectedDepartureDates': () => {},
        'onUpdate:returnDateFrom': () => {},
        'onUpdate:returnDateTo': () => {},
        'onUpdate:selectedReturnDates': () => {},
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

    expect(wrapper.findAll('.date-picker-stub')).toHaveLength(2)
  })
})
