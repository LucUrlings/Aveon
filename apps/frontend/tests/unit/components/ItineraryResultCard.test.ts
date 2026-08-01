import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ItineraryResultCard from '../../../src/features/itinerary-search/ItineraryResultCard.vue'

const result = {
  id: 'safe-id', bookingType: 'separateTickets', destinationOrder: ['AMS'], totalPrice: 100, currency: 'EUR', totalFlightDurationMinutes: 60,
  totalStops: 0, bookingCount: 1, airportSwitches: 0, stays: [],
  legs: [{ id: 'leg', originAirport: 'DUB', destinationAirport: 'AMS', departureLocalTime: '2026-09-01T09:00:00', arrivalLocalTime: '2026-09-01T10:00:00', durationMinutes: 60, stops: 0, segments: [] }],
  bookingOptions: [{ label: 'Book', url: 'https://book.example/fare?token=private', price: 100, currency: 'EUR', provider: 'FlightAPI' }],
  warnings: [], rankingBreakdown: { score: 1, totalPrice: 100, additionalFlightMinutes: 0, totalStops: 0, additionalBookings: 0, airportSwitches: 0 },
}

describe('ItineraryResultCard analytics', () => {
  const track = vi.fn()
  beforeEach(() => { track.mockReset(); window.umami = { track } })

  it('tracks result selection and booking clicks without the result id or booking URL', async () => {
    const wrapper = mount(ItineraryResultCard, { props: { result } })
    const explanation = wrapper.get('details.score-explanation')
    ;(explanation.element as HTMLDetailsElement).open = true
    await explanation.trigger('toggle')
    await wrapper.get('.booking-links a').trigger('click')

    expect(track).toHaveBeenCalledWith('result_selection', { booking_type: 'separateTickets' })
    expect(track).toHaveBeenCalledWith('booking_click', { booking_type: 'separateTickets', booking_count: 1, position: 1 })
    expect(JSON.stringify(track.mock.calls)).not.toContain('private')
    expect(JSON.stringify(track.mock.calls)).not.toContain('safe-id')
  })
})
