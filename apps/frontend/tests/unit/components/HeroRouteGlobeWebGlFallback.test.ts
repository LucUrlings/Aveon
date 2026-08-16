import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import HeroRouteGlobe from '../../../src/features/explore/HeroRouteGlobe.vue'

const { getHeroRoutes, globeFactory } = vi.hoisted(() => ({ getHeroRoutes: vi.fn(), globeFactory: vi.fn() }))
vi.mock('../../../src/features/explore/api', () => ({ getHeroRoutes }))
vi.mock('globe.gl', () => ({ default: class { constructor() { globeFactory() } } }))

const routerLinkStub = { props: ['to'], template: '<a :href="to"><slot /></a>' }

describe('HeroRouteGlobe flat map', () => {
  it('keeps the route summary and Explore call to action without loading WebGL', async () => {
    getHeroRoutes.mockResolvedValue({
      origin: { code: 'DUB', name: 'Dublin Airport', city: 'Dublin', country: 'Ireland', latitude: 53.42, longitude: -6.27 },
      destinations: [{ code: 'AMS', name: 'Amsterdam', city: 'Amsterdam', country: 'Netherlands', latitude: 52.31, longitude: 4.76 }],
      observedFrom: '2026-07-28', observedTo: '2026-08-07', fetchedAt: '2026-08-02T12:00:00Z', isComplete: true, isStale: false,
    })
    const wrapper = mount(HeroRouteGlobe, { global: { stubs: { RouterLink: routerLinkStub } } })
    await flushPromises()

    expect(globeFactory).not.toHaveBeenCalled()
    expect(wrapper.get('.flat-route-map svg').attributes('aria-label')).toContain('Dublin Airport')
    expect(wrapper.get('.hero-globe-caption').text()).toContain('1 current direct destination')
    expect(wrapper.get('.hero-globe-link').attributes('href')).toContain('/explore?path=DUB')
  })
})
