import { beforeEach, describe, expect, it, vi } from 'vitest'
import { trackItineraryEvent } from '../../../src/features/itinerary-search/analytics'

describe('multi-destination analytics', () => {
  const track = vi.fn()

  beforeEach(() => {
    track.mockReset()
    window.umami = { track }
  })

  it('emits the release events using only explicitly allowed non-sensitive properties', () => {
    trackItineraryEvent('completed_search', {
      mode: 'optimize', status: 'completed', coverage: 'bounded', result_count: 12,
      booking_url: 'https://provider.example/book?token=secret', search_id: 'private-id',
    })

    expect(track).toHaveBeenCalledWith('completed_search', {
      mode: 'optimize', status: 'completed', coverage: 'bounded', result_count: 12,
    })
  })

  it.each([
    'form_abandonment', 'validation_failure', 'completed_search', 'bounded_coverage', 'result_selection', 'booking_click',
  ] as const)('supports the %s event', event => {
    trackItineraryEvent(event)
    expect(track).toHaveBeenCalledWith(event, {})
  })
})
