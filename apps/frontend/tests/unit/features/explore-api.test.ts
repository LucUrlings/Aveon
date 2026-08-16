import { afterEach, describe, expect, it, vi } from 'vitest'
import { getExploreRoutes, getHeroRoutes } from '../../../src/features/explore/api'

describe('explore api', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('loads encoded origin and hero route networks', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({ origin: { code: 'DUB' }, destinations: [] }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    await getExploreRoutes('DUB')
    await getHeroRoutes()

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/explore/routes?origin=DUB')
    expect(fetchMock.mock.calls[1][0]).toBe('/api/v1/explore/hero')
    expect(fetchMock.mock.calls[0][1]).toMatchObject({ credentials: 'include' })
  })

  it('passes an exact departure date to the interactive route endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ origin: { code: 'DUB' }, destinations: [] }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await getExploreRoutes('DUB', '2026-09-18')

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/explore/routes?origin=DUB&departureDate=2026-09-18')
  })

  it('surfaces validation details from the backend', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ errors: { origin: ['Origin must be valid.'] } }), { status: 400, headers: { 'Content-Type': 'application/json' } })))

    await expect(getExploreRoutes('bad')).rejects.toThrow('Origin must be valid.')
  })
})
