import { afterEach, describe, expect, it, vi } from 'vitest'

const fetchMock = vi.fn()
vi.stubGlobal('fetch', fetchMock)

afterEach(() => fetchMock.mockReset())

describe('itinerary search api', () => {
  it('serializes time ranges with the backend range separator and list filters with commas', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({
      searchId: 'itinerary-1', mode: 'ordered', status: 'completed', phase: 'completed', progress: 100, results: [], warnings: [],
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    const { getItinerarySearch } = await import('../../../src/features/itinerary-search/api')

    await getItinerarySearch('itinerary-1', {
      departureTime: [60, 720], arrivalTime: [900, 1200], airlines: ['EI', 'KL'],
    })

    const url = new URL(fetchMock.mock.calls[0][0] as string, 'http://localhost')
    expect(url.searchParams.get('departureTime')).toBe('60-720')
    expect(url.searchParams.get('arrivalTime')).toBe('900-1200')
    expect(url.searchParams.get('airlines')).toBe('EI,KL')
  })

  it('explains a disabled deployment instead of exposing a raw 404', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 404 }))
    const { getItinerarySearchCapabilities } = await import('../../../src/features/itinerary-search/api')

    await expect(getItinerarySearchCapabilities()).rejects.toThrow('not enabled on this deployment')
  })
})
