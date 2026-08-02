import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import RouteGlobe from '../../../src/features/explore/RouteGlobe.vue'

const { globeFactory } = vi.hoisted(() => ({ globeFactory: vi.fn() }))
vi.mock('globe.gl', () => ({
  default: class {
    constructor(host: HTMLElement, options: unknown) { return globeFactory(host, options) }
  },
}))

const routes = {
  origin: { code: 'DUB', name: 'Dublin Airport', city: 'Dublin', country: 'Ireland', latitude: 53.42, longitude: -6.27 },
  destinations: [{ code: 'AMS', name: 'Amsterdam', city: 'Amsterdam', country: 'Netherlands', latitude: 52.31, longitude: 4.76 }],
  observedFrom: '2026-07-28', observedTo: '2026-08-07', fetchedAt: '2026-08-02T12:00:00Z', isComplete: true, isStale: false,
}

const createGlobe = () => {
  const controls = { autoRotate: true, autoRotateSpeed: 0, enablePan: true, enableZoom: true }
  const material = { color: { set: vi.fn() }, emissive: { set: vi.fn() }, emissiveIntensity: 0, shininess: 0 }
  const globe: Record<string, any> = { controls: () => controls, globeMaterial: () => material, _destructor: vi.fn() }
  let pointClick: ((point: unknown) => void) | undefined
  let pointHover: ((point: unknown) => void) | undefined
  for (const method of [
    'backgroundColor', 'showAtmosphere', 'atmosphereColor', 'atmosphereAltitude', 'showGraticules', 'polygonsData',
    'polygonAltitude', 'polygonCapColor', 'polygonSideColor', 'polygonStrokeColor', 'pointOfView',
    'pointsData', 'pointLat', 'pointLng', 'pointColor', 'pointRadius', 'pointAltitude', 'pointLabel', 'arcsData',
    'arcStartLat', 'arcStartLng', 'arcEndLat', 'arcEndLng', 'arcColor', 'arcStroke', 'arcAltitudeAutoScale',
    'arcDashLength', 'arcDashGap', 'arcDashInitialGap', 'arcDashAnimateTime', 'arcsTransitionDuration', 'width', 'height',
  ]) globe[method] = vi.fn(() => globe)
  globe.onPointClick = vi.fn((callback: (point: unknown) => void) => { pointClick = callback; return globe })
  globe.onPointHover = vi.fn((callback: (point: unknown) => void) => { pointHover = callback; return globe })
  return { globe, controls, clickPoint: (point: unknown) => pointClick?.(point), hoverPoint: (point: unknown) => pointHover?.(point) }
}

describe('RouteGlobe reduced motion', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })))
  })
  afterEach(() => vi.unstubAllGlobals())

  it('disables automatic rotation and arc animation', async () => {
    const fixture = createGlobe()
    globeFactory.mockReturnValue(fixture.globe)
    const wrapper = mount(RouteGlobe, { props: { routes } })
    await flushPromises()

    expect(fixture.controls.autoRotate).toBe(false)
    const animationTime = fixture.globe.arcDashAnimateTime.mock.calls.at(-1)?.[0] as (arc: { layer: string }) => number
    expect(animationTime({ layer: 'animation' })).toBe(0)
    wrapper.unmount()
    expect(fixture.globe._destructor).toHaveBeenCalledOnce()
  })

  it('configures route data, handles selection and focus, pauses rotation, resizes, and cleans up', async () => {
    vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })))
    const observe = vi.fn()
    const disconnect = vi.fn()
    vi.stubGlobal('ResizeObserver', class { observe = observe; disconnect = disconnect })
    const fixture = createGlobe()
    globeFactory.mockReturnValue(fixture.globe)
    const wrapper = mount(RouteGlobe, { props: { routes } })
    await flushPromises()

    expect(fixture.globe.pointsData).toHaveBeenCalledWith(expect.arrayContaining([
      expect.objectContaining({ code: 'DUB', origin: true }),
      expect.objectContaining({ code: 'AMS', origin: false }),
    ]))
    expect(fixture.globe.arcsData).toHaveBeenCalledWith([
      expect.objectContaining({ startLat: 53.42, startLng: -6.27, endLat: 52.31, endLng: 4.76, layer: 'animation' }),
    ])
    expect(fixture.globe.polygonsData).toHaveBeenCalledWith(expect.arrayContaining([expect.objectContaining({ type: 'Feature' })]))
    const animationTime = fixture.globe.arcDashAnimateTime.mock.calls.at(-1)?.[0] as (arc: { layer: string }) => number
    expect(animationTime({ layer: 'animation' })).toBe(2000)
    expect(animationTime({ layer: 'route' })).toBe(0)
    expect(fixture.globe.arcDashInitialGap).toHaveBeenCalledWith(0)
    expect(fixture.globe.arcsTransitionDuration).toHaveBeenCalledWith(0)
    expect(fixture.controls.autoRotate).toBe(true)
    expect(fixture.controls.enableZoom).toBe(true)
    expect(observe).toHaveBeenCalledOnce()
    expect(fixture.globe.width).toHaveBeenCalledWith(280)
    expect(fixture.globe.height).toHaveBeenCalledWith(320)

    fixture.clickPoint(routes.destinations[0])
    expect(wrapper.emitted('select')?.[0]).toEqual([routes.destinations[0]])
    fixture.clickPoint({ ...routes.origin, origin: true })
    expect(wrapper.emitted('select')).toHaveLength(1)
    fixture.hoverPoint({ ...routes.destinations[0], origin: false })
    expect(wrapper.emitted('hover')?.[0]).toEqual([expect.objectContaining({ code: 'AMS' })])
    fixture.hoverPoint(null)
    expect(wrapper.emitted('hover')?.[1]).toEqual([null])

    const jfk = { code: 'JFK', name: 'John F. Kennedy', city: 'New York', country: 'United States', latitude: 40.64, longitude: -73.78 }
    await wrapper.setProps({ selectedDestination: routes.destinations[0], committedPath: [routes.origin, routes.destinations[0], jfk] })
    const latestArcs = fixture.globe.arcsData.mock.calls.at(-1)?.[0]
    expect(latestArcs.filter((arc: { committed: boolean; layer: string }) => arc.committed && arc.layer === 'animation')).toHaveLength(2)
    expect(latestArcs.filter((arc: { committed: boolean; layer: string }) => arc.committed && arc.layer === 'route')).toHaveLength(2)

    await wrapper.get('.globe-canvas').trigger('pointerdown')
    expect(fixture.controls.autoRotate).toBe(false)
    await wrapper.get('.globe-canvas').trigger('pointerleave')
    expect(fixture.controls.autoRotate).toBe(true)
    await wrapper.get('.globe-canvas').trigger('wheel')
    expect(fixture.controls.autoRotate).toBe(false)
    ;(wrapper.vm as unknown as { focusDestination: (airport: typeof routes.destinations[number]) => void }).focusDestination(routes.destinations[0])
    expect(fixture.globe.pointOfView).toHaveBeenLastCalledWith({ lat: 52.31, lng: 4.76, altitude: 1.65 }, 900)

    wrapper.unmount()
    expect(disconnect).toHaveBeenCalledOnce()
    expect(fixture.globe._destructor).toHaveBeenCalledOnce()
  })

  it('disables zoom without capturing wheel interaction when requested', async () => {
    vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })))
    const fixture = createGlobe()
    globeFactory.mockReturnValue(fixture.globe)
    const wrapper = mount(RouteGlobe, { props: { routes, allowZoom: false } })
    await flushPromises()

    expect(fixture.controls.enableZoom).toBe(false)
    expect(fixture.controls.autoRotate).toBe(true)
    await wrapper.get('.globe-canvas').trigger('wheel')
    expect(fixture.controls.autoRotate).toBe(true)
  })

  it('emphasizes the prospective leg, dims other current arcs, and preserves committed arcs', async () => {
    vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })))
    const fixture = createGlobe()
    globeFactory.mockReturnValue(fixture.globe)
    const jfk = { code: 'JFK', name: 'John F. Kennedy', city: 'New York', country: 'United States', latitude: 40.64, longitude: -73.78 }
    const wrapper = mount(RouteGlobe, { props: { routes: { ...routes, destinations: [...routes.destinations, jfk] }, selectedDestination: routes.destinations[0], committedPath: [routes.origin, routes.destinations[0]] } })
    await flushPromises()

    const arcs = fixture.globe.arcsData.mock.calls.at(-1)?.[0] as Array<{ destination: typeof routes.destinations[number]; committed: boolean }>
    const color = fixture.globe.arcColor.mock.calls.at(-1)?.[0] as (arc: typeof arcs[number]) => string[]
    const stroke = fixture.globe.arcStroke.mock.calls.at(-1)?.[0] as (arc: typeof arcs[number]) => number
    const selected = arcs.find(arc => !arc.committed && arc.destination.code === 'AMS' && (arc as typeof arc & { layer: string }).layer === 'animation')!
    const selectedRoute = arcs.find(arc => !arc.committed && arc.destination.code === 'AMS' && (arc as typeof arc & { layer: string }).layer === 'route')!
    const other = arcs.find(arc => !arc.committed && arc.destination.code === 'JFK')!
    const committed = arcs.find(arc => arc.committed && (arc as typeof arc & { layer: string }).layer === 'animation')!

    expect(color(selected).join(' ')).toContain('rgba(245,158,11,.8)')
    expect(color(selectedRoute)).toBe('rgba(245,158,11,.3)')
    expect(color(other).join(' ')).toContain('rgba(79,70,229,.05)')
    expect(color(committed).join(' ')).toContain('rgba(167,139,250,.88)')
    expect(stroke(selected)).toBeGreaterThan(stroke(other))
    expect(stroke(committed)).toBeGreaterThan(stroke(other))
    wrapper.unmount()
  })

  it('restarts the emphasized route immediately and pans to a hovered destination', async () => {
    vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })))
    const fixture = createGlobe()
    globeFactory.mockReturnValue(fixture.globe)
    const wrapper = mount(RouteGlobe, { props: { routes } })
    await flushPromises()
    fixture.globe.arcsData.mockClear()
    fixture.globe.pointOfView.mockClear()

    await wrapper.setProps({ hoveredDestination: routes.destinations[0] })

    const arcs = fixture.globe.arcsData.mock.calls.at(-1)?.[0] as Array<{ destination: typeof routes.destinations[number]; layer: string }>
    expect(arcs.filter(arc => arc.destination.code === 'AMS').map(arc => arc.layer)).toEqual(['route', 'animation'])
    expect(fixture.globe.arcsTransitionDuration).toHaveBeenLastCalledWith(0)
    expect(fixture.globe.arcDashInitialGap).toHaveBeenLastCalledWith(0)
    expect(fixture.globe.pointOfView).toHaveBeenLastCalledWith({ lat: 52.31, lng: 4.76, altitude: 1.65 }, 450)
    expect(fixture.controls.autoRotate).toBe(false)

    await wrapper.setProps({ hoveredDestination: null })
    expect(fixture.controls.autoRotate).toBe(true)
    wrapper.unmount()
  })
})
