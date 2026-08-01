import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ItineraryFiltersPanel from '../../../src/features/itinerary-search/ItineraryFiltersPanel.vue'

describe('ItineraryFiltersPanel', () => {
  it('exposes shared complete-trip and advanced booking-risk filters', async () => {
    let model = { ranking: 'recommended' as const, allowAirportSwitches: true }
    const wrapper = mount(ItineraryFiltersPanel, {
      props: {
        modelValue: model,
        filters: {
          airlines: [{ value: 'TA', label: 'Test Air', count: 2 }],
          bookingSources: [{ value: 'FlightApi', label: 'FlightApi', count: 2 }],
          departureAirports: [], arrivalAirports: [], maxPrice: 500, maxBookingCount: 3,
        },
        'onUpdate:modelValue': (value) => { model = value },
      },
    })

    expect(wrapper.text()).toContain('Stops on every flight')
    expect(wrapper.text()).toContain('Maximum bookings')
    expect(wrapper.text()).toContain('Allow airport changes')

    await wrapper.findAll('input[type="checkbox"]')[3].setValue(true)
    expect(model).toMatchObject({ airlines: ['TA'], page: 1 })
  })
})
