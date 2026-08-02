import { describe, expect, it } from 'vitest'
import type { LocationQuery } from 'vue-router'
import { isLegacyRootSearch, router } from '../../../src/router'

Object.defineProperty(window, 'scrollTo', { value: () => {}, writable: true })

describe('frontend routes', () => {
  it('keeps the product index and flight search on separate routes', () => {
    expect(router.resolve('/').name).toBe('home')
    expect(router.resolve('/search').name).toBe('search')
    expect(router.resolve('/explore').name).toBe('explore')
    expect(typeof router.resolve('/explore').matched[0]?.components?.default).toBe('function')
  })

  it('redirects legacy root search URLs while leaving ordinary index queries alone', async () => {
    expect(isLegacyRootSearch('/', { origins: 'DUB', dates: '2026-09-01' } as LocationQuery)).toBe(true)
    expect(isLegacyRootSearch('/', { campaign: 'launch' } as LocationQuery)).toBe(false)
    expect(isLegacyRootSearch('/search', { origins: 'DUB' } as LocationQuery)).toBe(false)

    await router.push('/?origins=DUB&destinations=AMS&dates=2026-09-01')
    expect(router.currentRoute.value.path).toBe('/search')
    expect(router.currentRoute.value.query).toMatchObject({
      origins: 'DUB',
      destinations: 'AMS',
      dates: '2026-09-01',
    })

    await router.push('/?campaign=launch')
    expect(router.currentRoute.value.path).toBe('/')
    expect(router.currentRoute.value.query.campaign).toBe('launch')
  })
})
