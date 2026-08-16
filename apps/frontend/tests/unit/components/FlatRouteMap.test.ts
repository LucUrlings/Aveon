import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import FlatRouteMap from '../../../src/features/explore/FlatRouteMap.vue'

const routes = {
  origin: { code: 'DUB', name: 'Dublin Airport', city: 'Dublin', country: 'Ireland', latitude: 53.42, longitude: -6.27 },
  destinations: [
    { code: 'AMS', name: 'Amsterdam Airport Schiphol', city: 'Amsterdam', country: 'Netherlands', latitude: 52.31, longitude: 4.76 },
    { code: 'JFK', name: 'John F. Kennedy International Airport', city: 'New York', country: 'United States', latitude: 40.64, longitude: -73.78 },
  ],
  observedFrom: '2026-07-28', observedTo: '2026-08-07', fetchedAt: '2026-08-02T12:00:00Z', isComplete: true, isStale: false,
}

describe('FlatRouteMap', () => {
  it('renders geographic countries and routes and selects destinations by click or keyboard', async () => {
    const wrapper = mount(FlatRouteMap, { props: { routes } })

    expect(wrapper.findAll('.map-country').length).toBeGreaterThan(100)
    expect(wrapper.findAll('.map-route')).toHaveLength(2)
    expect(wrapper.text()).toContain('Amsterdam')
    expect(wrapper.text()).toContain('New York')
    expect(wrapper.get('svg').attributes('aria-label')).toContain('Dublin Airport')

    const destinations = wrapper.findAll('.map-destination')
    await destinations[0].trigger('click')
    await destinations[1].trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('select')).toEqual([[routes.destinations[0]], [routes.destinations[1]]])
  })

  it('plots up to thirty destinations without labeling every dense point', () => {
    const destinations = Array.from({ length: 60 }, (_, index) => ({
      code: `X${String(index).padStart(3, '0')}`,
      name: `Test Airport ${index}`,
      city: `City ${index}`,
      country: 'Test Country',
      latitude: -35 + (index % 12) * 9,
      longitude: -170 + index * 5.5,
    }))
    const wrapper = mount(FlatRouteMap, { props: { routes: { ...routes, destinations } } })

    expect(wrapper.findAll('.map-route')).toHaveLength(30)
    expect(wrapper.findAll('.map-destination')).toHaveLength(30)
    expect(wrapper.findAll('.map-destination text').length).toBeLessThanOrEqual(18)
  })
})
