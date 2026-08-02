import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import HeroRouteGlobe from '../../../src/features/explore/HeroRouteGlobe.vue'

const { getHeroRoutes } = vi.hoisted(() => ({ getHeroRoutes: vi.fn() }))
vi.mock('../../../src/features/explore/api', () => ({ getHeroRoutes }))
vi.mock('globe.gl', () => ({ default: class { constructor() { throw new Error('WebGL unavailable') } } }))

const routerLinkStub = { props: ['to'], template: '<a :href="to"><slot /></a>' }

describe('HeroRouteGlobe WebGL fallback', () => {
  it('keeps the static route summary and Explore call to action when WebGL fails', async () => {
    getHeroRoutes.mockResolvedValue({
      origin: { code: 'DUB', name: 'Dublin Airport', city: 'Dublin', country: 'Ireland', latitude: 53.42, longitude: -6.27 },
      destinations: [{ code: 'AMS', name: 'Amsterdam', city: 'Amsterdam', country: 'Netherlands', latitude: 52.31, longitude: 4.76 }],
      observedFrom: '2026-07-28', observedTo: '2026-08-07', fetchedAt: '2026-08-02T12:00:00Z', isComplete: true, isStale: false,
    })
    const wrapper = mount(HeroRouteGlobe, { global: { stubs: { RouterLink: routerLinkStub } } })
    await flushPromises()

    expect(wrapper.get('.globe-fallback').text()).toContain('DUB')
    expect(wrapper.get('.globe-fallback').text()).toContain('1 current direct destination')
    expect(wrapper.get('.hero-globe-link').attributes('href')).toBe('/explore')
  })
})
