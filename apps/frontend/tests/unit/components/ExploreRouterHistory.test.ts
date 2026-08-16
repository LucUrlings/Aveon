import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import ExplorePage from '../../../src/pages/ExplorePage.vue'

const { getExploreRoutes, getItinerarySearchCapabilities } = vi.hoisted(() => ({ getExploreRoutes: vi.fn(), getItinerarySearchCapabilities: vi.fn() }))
vi.mock('../../../src/features/explore/api', () => ({ getExploreRoutes }))
vi.mock('../../../src/features/itinerary-search/api', () => ({ getItinerarySearchCapabilities }))
vi.mock('../../../src/features/explore/RouteGlobe.vue', () => ({
  default: {
    props: ['routes', 'selectedDestination', 'hoveredDestination', 'committedPath'],
    emits: ['select', 'hover'],
    methods: { focusDestination: vi.fn() },
    template: '<div class="route-globe-stub" />',
  },
}))

const dub = { code: 'DUB', name: 'Dublin Airport', city: 'Dublin', country: 'Ireland', latitude: 53.42, longitude: -6.27 }
const ams = { code: 'AMS', name: 'Amsterdam Schiphol', city: 'Amsterdam', country: 'Netherlands', latitude: 52.31, longitude: 4.76 }
const jfk = { code: 'JFK', name: 'John F. Kennedy International', city: 'New York', country: 'United States', latitude: 40.64, longitude: -73.78 }
const response = (origin: typeof dub, destinations: typeof dub[]) => ({
  origin, destinations, observedFrom: '2026-07-28', observedTo: '2026-08-07', fetchedAt: '2026-08-02T12:00:00Z', isComplete: true, isStale: false,
})

describe('Explore router history', () => {
  it('restores the previous committed path through real router Back navigation', async () => {
    getItinerarySearchCapabilities.mockResolvedValue({ providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 8 })
    getExploreRoutes.mockImplementation((code: string) => Promise.resolve(code === 'AMS' ? response(ams, [jfk]) : response(dub, [ams])))
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/explore', component: ExplorePage },
        { path: '/search', component: { template: '<div />' } },
        { path: '/multi-destination', component: { template: '<div />' } },
      ],
    })
    await router.push('/explore?path=DUB')
    await router.isReady()
    const wrapper = mount(ExplorePage, { global: { plugins: [router] } })
    await flushPromises()

    await wrapper.get('.destination-browser li button').trigger('click')
    const onward = wrapper.findAll('.selection-actions button').find(button => button.text().includes('Explore onward'))!
    const forward = new Promise<void>(resolve => { const remove = router.afterEach(() => { remove(); resolve() }) })
    await onward.trigger('click')
    await forward
    await flushPromises()
    expect(router.currentRoute.value.query.path).toBe('DUB,AMS')
    expect(wrapper.get('.route-tray').text()).toContain('Dublin→Amsterdam')

    const backward = new Promise<void>(resolve => { const remove = router.afterEach(() => { remove(); resolve() }) })
    router.back()
    await backward
    await flushPromises()
    expect(router.currentRoute.value.query.path).toBe('DUB')
    expect(wrapper.get('.route-tray').text()).not.toContain('AMS')
    wrapper.unmount()
  })
})
