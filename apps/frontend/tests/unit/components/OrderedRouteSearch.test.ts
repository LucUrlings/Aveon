import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import OrderedLegEditor, { type OrderedLegModel } from '../../../src/features/itinerary-search/OrderedLegEditor.vue'
import OrderedRouteSearch from '../../../src/features/itinerary-search/OrderedRouteSearch.vue'

describe('OrderedRouteSearch', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('serializes every dynamic airport group and continuity rule', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) => Promise.resolve(new Response(JSON.stringify(url.includes('/configuration')
      ? { providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 8 }
      : { searchId: 'ordered-1', mode: 'ordered', status: 'running', phase: 'searching', progress: 0, results: [], warnings: [] }
    ), { status: url.includes('/configuration') ? 200 : 202, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(OrderedRouteSearch)
    await wrapper.get('button.secondary-action').trigger('click')
    const airport = (code: string) => ({ code, name: code, displayLabel: code })
    const models: OrderedLegModel[] = [
      { id: 'one', fromLabel: 'Ireland', toLabel: 'Netherlands', from: [airport('DUB'), airport('SNN')], to: [airport('AMS')], departureDate: '2026-09-01', continuity: 'sameAirport' },
      { id: 'two', fromLabel: 'Netherlands', toLabel: 'France', from: [airport('AMS'), airport('RTM')], to: [airport('CDG')], departureDate: '2026-09-03', continuity: 'allowSwitch' },
    ]
    wrapper.findAllComponents(OrderedLegEditor).forEach((editor, index) => editor.vm.$emit('update:modelValue', models[index]))
    await wrapper.vm.$nextTick()
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const postCall = fetchMock.mock.calls.find(([, options]) => options?.method === 'POST')!
    const request = JSON.parse(postCall[1].body)
    expect(request).toMatchObject({ mode: 'ordered', adults: 1, cabinClass: 'economy', ranking: 'recommended' })
    expect(request.legs).toHaveLength(2)
    expect(request.legs[0].from.airportCodes).toEqual(['DUB', 'SNN'])
    expect(request.legs[0].from.label).toBe('Ireland')
    expect(request.legs[1]).toMatchObject({ airportContinuityWithPrevious: 'allowSwitch', departureDate: '2026-09-03' })
    wrapper.unmount()
  })

  it('uses server-provided ordered-leg and airport-group limits', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 1, maxTripDays: 31, maxOrderedLegs: 2,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(OrderedRouteSearch)
    await flushPromises()

    const add = wrapper.get('button.secondary-action')
    await add.trigger('click')
    expect(wrapper.findAll('fieldset.ordered-leg')).toHaveLength(2)
    expect(add.attributes('disabled')).toBeDefined()
    expect(wrapper.findAllComponents(OrderedLegEditor).every(editor => editor.props('maxAirports') === 1)).toBe(true)
    wrapper.unmount()
  })
})
