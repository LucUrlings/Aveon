import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AirportGroupPicker from '../../../src/components/flight-search/AirportGroupPicker.vue'
import { localDateWithOffset } from '../../../src/features/explore/localDate'
import RouteGlobe from '../../../src/features/explore/RouteGlobe.vue'
import ExplorePage from '../../../src/pages/ExplorePage.vue'

const { getExploreRoutes, getItinerarySearchCapabilities, push, replace, routeState } = vi.hoisted(() => ({ getExploreRoutes: vi.fn(), getItinerarySearchCapabilities: vi.fn(), push: vi.fn(), replace: vi.fn(), routeState: { current: null as any } }))

vi.mock('../../../src/features/explore/api', () => ({ getExploreRoutes }))
vi.mock('../../../src/features/itinerary-search/api', () => ({ getItinerarySearchCapabilities }))
vi.mock('vue-router', async () => {
  const { reactive } = await import('vue')
  routeState.current = reactive({ query: {} as Record<string, string> })
  return { useRouter: () => ({ push, replace }), useRoute: () => routeState.current }
})
vi.mock('../../../src/features/explore/RouteGlobe.vue', () => ({
  default: {
    props: ['routes', 'selectedDestination', 'hoveredDestination', 'committedPath'],
    emits: ['select', 'hover'],
    template: '<button class="globe-destination" @click="$emit(\'select\', routes.destinations[0])">Globe destination</button>',
  },
}))

const network = {
  origin: { code: 'DUB', name: 'Dublin Airport', city: 'Dublin', country: 'Ireland', latitude: 53.42, longitude: -6.27 },
  destinations: [
    { code: 'AMS', name: 'Amsterdam Schiphol', city: 'Amsterdam', country: 'Netherlands', latitude: 52.31, longitude: 4.76 },
    { code: 'JFK', name: 'John F. Kennedy International', city: 'New York', country: 'United States', latitude: 40.64, longitude: -73.78 },
  ],
  observedFrom: '2026-07-28', observedTo: '2026-08-07', fetchedAt: '2026-08-02T12:00:00Z', isComplete: true, isStale: false,
}

describe('ExplorePage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    for (const key of Object.keys(routeState.current.query)) delete routeState.current.query[key]
    getExploreRoutes.mockResolvedValue(network)
    getItinerarySearchCapabilities.mockResolvedValue({ providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 8 })
  })

  it('loads an origin, previews destinations, and only hands off after an explicit action', async () => {
    vi.spyOn(Math, 'random').mockReturnValue(0)
    const wrapper = mount(ExplorePage)
    const leaveDate = wrapper.get('.leave-date input')
    expect(leaveDate.attributes('min')).toBe(localDateWithOffset(0))
    expect(leaveDate.attributes('max')).toBe(localDateWithOffset(365))
    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' })
    await flushPromises()

    expect(getExploreRoutes).toHaveBeenCalledWith('DUB', expect.any(String), expect.any(AbortSignal))
    expect(wrapper.get('#routes-heading').text()).toContain('2 destinations')
    expect(wrapper.get('.destination-browser').text()).toContain('Amsterdam')
    await wrapper.get('.globe-destination').trigger('click')
    expect(push).not.toHaveBeenCalled()
    expect(wrapper.get('.route-selection').text()).toContain('Dublin → Amsterdam')
    const globeColumnHtml = wrapper.get('.globe-column').html()
    expect(globeColumnHtml.indexOf('route-selection')).toBeLessThan(globeColumnHtml.indexOf('globe-destination'))
    await wrapper.get('.primary-selection').trigger('click')
    expect(push).toHaveBeenCalledWith({ path: '/search', query: { origins: 'DUB', destinations: 'AMS', dates: expect.any(String), prefill: 'true' } })

    await wrapper.get('.randomize-button').trigger('click')
    expect(wrapper.get('.route-selection').text()).toContain('Selected direct route')
    vi.restoreAllMocks()
  })

  it('announces loading and offers retry after a provider error', async () => {
    let resolveRequest!: (value: typeof network) => void
    getExploreRoutes.mockReturnValueOnce(new Promise(resolve => { resolveRequest = resolve }))
    const wrapper = mount(ExplorePage)
    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' })
    await wrapper.vm.$nextTick()

    expect(wrapper.get('[role="status"]').text()).toContain('Mapping direct destinations')
    resolveRequest(network)
    await flushPromises()

    getExploreRoutes.mockRejectedValueOnce(new Error('Schedule provider unavailable'))
    await wrapper.get('.airport-chip').trigger('click')
    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'AMS', name: 'Amsterdam Schiphol', displayLabel: 'Amsterdam Schiphol (AMS)' })
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('Schedule provider unavailable')

    getExploreRoutes.mockResolvedValueOnce({ ...network, origin: network.destinations[0] })
    await wrapper.get('[role="alert"] button').trigger('click')
    await flushPromises()
    expect(wrapper.get('#routes-heading').text()).toContain('2 destinations')
  })

  it('retries the complete shared URL path after its initial hydration fails', async () => {
    const amsNetwork = {
      ...network,
      origin: network.destinations[0],
      destinations: [network.destinations[1]],
    }
    routeState.current.query.path = 'DUB,AMS'
    getExploreRoutes.mockRejectedValueOnce(new Error('Schedule provider unavailable'))
    const wrapper = mount(ExplorePage)
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('Schedule provider unavailable')

    getExploreRoutes.mockImplementation((code: string) => Promise.resolve(code === 'AMS' ? amsNetwork : network))
    await wrapper.get('[role="alert"] button').trigger('click')
    await flushPromises()

    expect(getExploreRoutes).toHaveBeenLastCalledWith('AMS', undefined, expect.any(AbortSignal))
    expect(wrapper.get('.route-tray').text()).toContain('Dublin→Amsterdam')
    expect(wrapper.get('#routes-heading').text()).toContain('Amsterdam connects directly')
  })

  it('shows stale, partial, empty, and filtered destination states accessibly', async () => {
    getExploreRoutes.mockResolvedValueOnce({ ...network, isStale: true, isComplete: false })
    const wrapper = mount(ExplorePage)
    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' })
    await flushPromises()

    expect(wrapper.text()).toContain('latest cached schedule')
    expect(wrapper.text()).toContain('Some scheduled destinations could not be included, so this map is partial.')
    expect(wrapper.text()).toContain('Direct departures scheduled for')
    const destinationBrowser = wrapper.get('.destination-browser')
    expect(destinationBrowser.attributes('aria-labelledby')).toBe('destination-list-heading')
    expect(destinationBrowser.get('ul').exists()).toBe(true)
    expect(destinationBrowser.findAll('li button')).toHaveLength(2)
    expect(destinationBrowser.findAll('li button').every(button => button.element.tagName === 'BUTTON')).toBe(true)
    await wrapper.get('.destination-browser input').setValue('New York')
    expect(wrapper.get('.destination-browser').text()).toContain('JFK')
    expect(wrapper.get('.destination-browser').text()).not.toContain('AMS')

    getExploreRoutes.mockResolvedValueOnce({ ...network, origin: network.destinations[0], destinations: [] })
    await wrapper.get('.airport-chip').trigger('click')
    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'AMS', name: 'Amsterdam Schiphol', displayLabel: 'Amsterdam Schiphol (AMS)' })
    await flushPromises()
    expect(wrapper.get('.empty-network').text()).toContain('No direct destinations were found')
    expect(wrapper.get('.randomize-button').attributes()).toHaveProperty('disabled')
  })

  it('keeps the newest airport request in control of the loading state', async () => {
    let resolveNewest!: (value: typeof network) => void
    getExploreRoutes
      .mockImplementationOnce((_origin: string, _departureDate: string, signal: AbortSignal) => new Promise((_resolve, reject) => {
        signal.addEventListener('abort', () => reject(Object.assign(new Error('aborted'), { name: 'AbortError' })))
      }))
      .mockImplementationOnce(() => new Promise(resolve => { resolveNewest = resolve }))
    const wrapper = mount(ExplorePage)

    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' })
    await wrapper.vm.$nextTick()
    await wrapper.get('.airport-chip').trigger('click')
    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'AMS', name: 'Amsterdam Schiphol', displayLabel: 'Amsterdam Schiphol (AMS)' })
    await flushPromises()

    expect(wrapper.get('[role="status"]').text()).toContain('Mapping direct destinations')
    resolveNewest({ ...network, origin: { ...network.origin, code: 'AMS', city: 'Amsterdam' } })
    await flushPromises()
    expect(wrapper.get('#routes-heading').text()).toContain('Amsterdam connects directly')
  })

  it('selects without navigating from the keyboard-accessible route list', async () => {
    const wrapper = mount(ExplorePage)
    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' })
    await flushPromises()
    const destinationButton = wrapper.get('.destination-browser li button')

    await destinationButton.trigger('keydown', { key: 'Enter' })

    expect(push).not.toHaveBeenCalled()
    expect(destinationButton.attributes('aria-pressed')).toBe('true')
    expect(wrapper.get('.route-selection').text()).toContain('Dublin → Amsterdam')
  })

  it('replaces candidates, clears a candidate, and clears the complete route and URL', async () => {
    const wrapper = mount(ExplorePage)
    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' })
    await flushPromises()
    const destinations = wrapper.findAll('.destination-browser li button')

    await destinations[0].trigger('focus')
    expect(wrapper.getComponent(RouteGlobe).props('hoveredDestination')).toMatchObject({ code: 'AMS' })
    await destinations[0].trigger('blur')
    expect(wrapper.getComponent(RouteGlobe).props('hoveredDestination')).toBeNull()
    await destinations[0].trigger('click')
    expect(wrapper.get('.route-selection').text()).toContain('Dublin → Amsterdam')
    await destinations[1].trigger('click')
    expect(wrapper.get('.route-selection').text()).toContain('Dublin → New York')
    expect(wrapper.get('.route-selection').text()).not.toContain('Dublin → Amsterdam')
    const clear = wrapper.findAll('.selection-actions button').find(button => button.text() === 'Clear selection')!
    await clear.trigger('click')
    expect(wrapper.find('.route-selection').exists()).toBe(false)
    expect(wrapper.find('.route-tray').exists()).toBe(true)
    expect(wrapper.find('.airport-chip').exists()).toBe(true)
    expect(wrapper.find('.explore-results').exists()).toBe(true)

    await wrapper.get('.tray-clear').trigger('click')
    expect(wrapper.find('.route-tray').exists()).toBe(false)
    expect(wrapper.find('.airport-chip').exists()).toBe(false)
    expect(wrapper.find('.explore-results').exists()).toBe(false)
    expect(replace).toHaveBeenLastCalledWith({ path: '/explore' })
  })

  it('commits onward exploration to URL state and exposes recovery controls', async () => {
    const wrapper = mount(ExplorePage)
    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' })
    await flushPromises()
    await wrapper.get('.destination-browser li button').trigger('click')
    const onward = wrapper.findAll('.selection-actions button').find(button => button.text().includes('Explore onward'))!
    await onward.trigger('click')

    expect(push).toHaveBeenCalledWith({ path: '/explore', query: { path: 'DUB,AMS', date: expect.any(String) } })
    expect(wrapper.get('.route-tray').text()).toContain('Dublin')
  })

  it('hydrates URL paths, restores an earlier path, and hands a preview to Build my route', async () => {
    const amsNetwork = {
      ...network,
      origin: network.destinations[0],
      destinations: [network.destinations[1]],
    }
    getExploreRoutes.mockImplementation((code: string) => Promise.resolve(code === 'AMS' ? amsNetwork : network))
    routeState.current.query.path = 'DUB,AMS'
    const wrapper = mount(ExplorePage)
    await flushPromises()

    expect(getExploreRoutes).toHaveBeenCalledWith('DUB', expect.any(String), expect.any(AbortSignal))
    expect(getExploreRoutes).toHaveBeenCalledWith('AMS', undefined, expect.any(AbortSignal))
    expect(wrapper.get('.route-tray').text()).toContain('Dublin→Amsterdam')
    await wrapper.get('.destination-browser li button').trigger('click')
    await wrapper.get('.primary-selection').trigger('click')
    expect(push).toHaveBeenCalledWith({ path: '/build-route', query: { route: 'DUB,AMS,JFK', departureDate: expect.any(String), source: 'explore', prefill: 'true' } })

    routeState.current.query.path = 'DUB'
    await flushPromises()
    expect(wrapper.get('.route-tray').text()).not.toContain('AMS')
    expect(wrapper.get('#routes-heading').text()).toContain('Dublin')
  })

  it('rejects a shared path when an adjacent scheduled edge was not observed', async () => {
    const cdg = { code: 'CDG', name: 'Charles de Gaulle', city: 'Paris', country: 'France', latitude: 49.01, longitude: 2.55 }
    getExploreRoutes.mockImplementation((code: string) => Promise.resolve(code === 'CDG' ? { ...network, origin: cdg } : network))
    routeState.current.query.path = 'DUB,CDG'
    const wrapper = mount(ExplorePage)
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('No current direct schedule was found from DUB to CDG')
    expect(wrapper.find('.explore-results').exists()).toBe(false)
  })

  it('wires breadcrumb truncation and removing the last stop to committed URL paths', async () => {
    const amsNetwork = { ...network, origin: network.destinations[0], destinations: [network.destinations[1]] }
    const jfkNetwork = { ...network, origin: network.destinations[1], destinations: [] }
    getExploreRoutes.mockImplementation((code: string) => Promise.resolve(code === 'AMS' ? amsNetwork : code === 'JFK' ? jfkNetwork : network))
    routeState.current.query.path = 'DUB,AMS,JFK'
    const wrapper = mount(ExplorePage)
    await flushPromises()

    await wrapper.get('[aria-label="Return to Dublin"]').trigger('click')
    expect(push).toHaveBeenCalledWith({ path: '/explore', query: { path: 'DUB', date: expect.any(String) } })
    await wrapper.get('.tray-recovery').trigger('click')
    expect(push).toHaveBeenCalledWith({ path: '/explore', query: { path: 'DUB,AMS', date: expect.any(String) } })
  })

  it('keeps a newly committed path visible when its onward network fails', async () => {
    getExploreRoutes.mockImplementation((code: string) => code === 'AMS'
      ? Promise.reject(new Error('Onward schedule unavailable'))
      : Promise.resolve(network))
    const wrapper = mount(ExplorePage)
    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' })
    await flushPromises()
    await wrapper.get('.destination-browser li button').trigger('click')
    const onward = wrapper.findAll('.selection-actions button').find(button => button.text().includes('Explore onward'))!
    await onward.trigger('click')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('Onward schedule unavailable')
    expect(wrapper.get('.route-tray').text()).toContain('Dublin→Amsterdam')
  })

  it('announces stale schedule data after moving to an onward origin', async () => {
    const amsNetwork = { ...network, origin: network.destinations[0], destinations: [network.destinations[1]], isStale: true }
    getExploreRoutes.mockImplementation((code: string) => Promise.resolve(code === 'AMS' ? amsNetwork : network))
    const wrapper = mount(ExplorePage)
    wrapper.getComponent(AirportGroupPicker).vm.$emit('addAirport', { code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' })
    await flushPromises()
    await wrapper.get('.destination-browser li button').trigger('click')
    const onward = wrapper.findAll('.selection-actions button').find(button => button.text().includes('Explore onward'))!
    await onward.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('latest cached schedule')
    expect(wrapper.get('.route-tray').text()).toContain('Dublin→Amsterdam')
  })

  it('rejects route cycles and keeps the committed builder handoff at the leg limit', async () => {
    getItinerarySearchCapabilities.mockResolvedValueOnce({ providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 1 })
    const amsNetwork = { ...network, origin: network.destinations[0], destinations: [network.origin, network.destinations[1]] }
    getExploreRoutes.mockImplementation((code: string) => Promise.resolve(code === 'AMS' ? amsNetwork : network))
    routeState.current.query.path = 'DUB,AMS'
    const wrapper = mount(ExplorePage)
    await flushPromises()

    const repeatedAirport = wrapper.findAll('.destination-browser li button').find(button => button.text().includes('DUB'))!
    expect(repeatedAirport.attributes('disabled')).toBeDefined()
    const newAirport = wrapper.findAll('.destination-browser li button').find(button => button.text().includes('JFK'))!
    await newAirport.trigger('click')
    const keepExploring = wrapper.findAll('.selection-actions button').find(button => button.text().includes('Keep exploring'))!
    expect(keepExploring.attributes('disabled')).toBeDefined()
    await wrapper.get('.primary-selection').trigger('click')
    expect(push).toHaveBeenCalledWith({ path: '/build-route', query: { route: 'DUB,AMS', departureDate: expect.any(String), source: 'explore', prefill: 'true' } })
  })
})
