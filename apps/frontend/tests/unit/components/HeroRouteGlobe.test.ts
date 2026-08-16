import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import HeroRouteGlobe from '../../../src/features/explore/HeroRouteGlobe.vue'

const { getHeroRoutes } = vi.hoisted(() => ({ getHeroRoutes: vi.fn() }))
vi.mock('../../../src/features/explore/api', () => ({ getHeroRoutes }))

const routerLinkStub = { props: ['to'], template: '<a :href="to"><slot /></a>' }
const routeGlobeStub = { props: ['routes', 'interactive', 'allowZoom'], template: '<div class="route-globe-stub">{{ routes && routes.origin ? routes.origin.code : \'map\' }}</div>' }

describe('HeroRouteGlobe', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows a random hub preview and links to Explore', async () => {
    getHeroRoutes.mockResolvedValue({
      origin: { code: 'DUB', name: 'Dublin Airport', city: 'Dublin', country: 'Ireland', latitude: 53.42, longitude: -6.27 },
      destinations: [{ code: 'AMS', name: 'Amsterdam', city: 'Amsterdam', country: 'Netherlands', latitude: 52.31, longitude: 4.76 }],
      observedFrom: '2026-07-28', observedTo: '2026-08-07', fetchedAt: '2026-08-02T12:00:00Z', isComplete: true, isStale: false,
    })
    const wrapper = mount(HeroRouteGlobe, { global: { stubs: { RouterLink: routerLinkStub, RouteGlobe: routeGlobeStub } } })
    await flushPromises()

    expect(wrapper.get('.route-globe-stub').text()).toBe('DUB')
    expect(wrapper.getComponent(routeGlobeStub).props('interactive')).toBe(false)
    expect(wrapper.getComponent(routeGlobeStub).props('allowZoom')).toBe(false)
    expect(wrapper.get('.hero-globe-caption').text()).toContain('Dublin · DUB')
    expect(wrapper.get('.hero-globe-caption').text()).toContain('1 current direct destination')
    expect(wrapper.get('.hero-globe-link').attributes('href')).toBe('/explore')
  })

  it('keeps a useful Explore fallback when the preview request fails', async () => {
    getHeroRoutes.mockRejectedValue(new Error('unavailable'))
    const wrapper = mount(HeroRouteGlobe, { global: { stubs: { RouterLink: routerLinkStub, RouteGlobe: routeGlobeStub } } })
    await flushPromises()

    expect(wrapper.get('.route-globe-stub').text()).toBe('map')
    expect(wrapper.get('.hero-globe-status').text()).toContain('Route preview unavailable')
    expect(wrapper.get('.hero-globe-link').attributes('href')).toBe('/explore')
  })

  it('labels a fast first-page response as a quick preview', async () => {
    getHeroRoutes.mockResolvedValue({
      origin: { code: 'DXB', name: 'Dubai International', city: 'Dubai', country: 'United Arab Emirates', latitude: 25.25, longitude: 55.36 },
      destinations: [{ code: 'AMS', name: 'Amsterdam', city: 'Amsterdam', country: 'Netherlands', latitude: 52.31, longitude: 4.76 }],
      observedFrom: '2026-08-04', observedTo: '2026-08-14', fetchedAt: '2026-08-09T12:00:00Z', isComplete: false, isStale: false,
    })
    const wrapper = mount(HeroRouteGlobe, { global: { stubs: { RouterLink: routerLinkStub, RouteGlobe: routeGlobeStub } } })
    await flushPromises()

    expect(wrapper.get('.hero-globe-caption').text()).toContain('1+ routes in this quick preview')
  })

  it('announces the preview while its lazy request is loading', () => {
    getHeroRoutes.mockReturnValue(new Promise(() => {}))
    const wrapper = mount(HeroRouteGlobe, { global: { stubs: { RouterLink: routerLinkStub, RouteGlobe: routeGlobeStub } } })

    expect(wrapper.get('.route-globe-stub').text()).toBe('map')
    expect(wrapper.get('[role="status"]').text()).toContain('Loading routes')
    expect(wrapper.get('.hero-globe-link').attributes('href')).toBe('/explore')
  })
})
