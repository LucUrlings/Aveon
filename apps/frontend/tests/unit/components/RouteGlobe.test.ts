import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import RouteGlobe from '../../../src/features/explore/RouteGlobe.vue'

vi.mock('globe.gl', () => ({ default: class { constructor() { throw new Error('WebGL unavailable') } } }))

describe('RouteGlobe', () => {
  it('provides meaningful non-canvas content when WebGL cannot initialize', async () => {
    const wrapper = mount(RouteGlobe, {
      props: {
        routes: {
          origin: { code: 'DUB', name: 'Dublin Airport', city: 'Dublin', country: 'Ireland', latitude: 53.42, longitude: -6.27 },
          destinations: [{ code: 'AMS', name: 'Amsterdam', city: 'Amsterdam', country: 'Netherlands', latitude: 52.31, longitude: 4.76 }],
          observedFrom: '2026-07-28', observedTo: '2026-08-07', fetchedAt: '2026-08-02T12:00:00Z', isComplete: true, isStale: false,
        },
      },
    })
    await flushPromises()

    expect(wrapper.get('.globe-fallback').attributes('aria-label')).toContain('Dublin')
    expect(wrapper.get('.globe-fallback').text()).toContain('1 current direct destination')
  })
})
