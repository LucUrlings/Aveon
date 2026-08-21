import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import OrderedLegEditor, { type OrderedLegModel } from '../../../src/features/itinerary-search/OrderedLegEditor.vue'
import OrderedRouteSearch from '../../../src/features/itinerary-search/OrderedRouteSearch.vue'

describe('OrderedRouteSearch', () => {
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('loads airport suggestions when a shared picker input changes', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      airports: [{ code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' }],
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(OrderedLegEditor, {
      props: {
        modelValue: {
          id: 'one', fromLabel: 'Ireland', toLabel: 'Netherlands', from: [], to: [],
          departureDate: '2026-09-01', continuity: 'sameAirport',
        },
        index: 0,
        removable: false,
        maxAirports: 5,
      },
    })

    await wrapper.get('[aria-label="Add a starting airport or city"]').setValue('dub')
    await vi.advanceTimersByTimeAsync(200)
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/airports?query=dub',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
    expect(wrapper.get('[aria-label="Starting airport suggestions"]').text()).toContain('Dublin Airport (DUB)')
    wrapper.unmount()
  })

  it('connects each new flight to the preceding destination and serializes its continuity rule', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) => Promise.resolve(new Response(JSON.stringify(url.includes('/configuration')
      ? { providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 8 }
      : {
          searchId: 'ordered-1', mode: 'ordered', status: 'completed', phase: 'completed', progress: 100, results: [],
          warnings: [{ code: 'noCompleteItinerary', message: 'No complete itinerary could be built.' }],
          orderedLegs: [
            { legId: 'one', fromLabel: 'Ireland', toLabel: 'Netherlands', fromAirportCodes: ['DUB', 'SNN'], toAirportCodes: ['AMS'], departureDate: '2026-09-01', status: 'faresFound', airportPairsPlanned: 2, airportPairsScheduled: 2, airportPairsCompleted: 2, faresFound: 3, failedPairs: 0 },
            { legId: 'two', fromLabel: 'Netherlands', toLabel: 'France', fromAirportCodes: ['AMS'], toAirportCodes: ['CDG'], departureDate: '2026-09-03', status: 'noFares', airportPairsPlanned: 1, airportPairsScheduled: 1, airportPairsCompleted: 1, faresFound: 0, failedPairs: 0 },
          ],
        }
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
    expect(request.legs[1].from.airportCodes).toEqual(['AMS'])
    expect(request.legs[1].from.label).toBe('Netherlands')
    expect(request.legs[1]).toMatchObject({ airportContinuityWithPrevious: 'allowSwitch', departureDate: '2026-09-03' })
    expect(wrapper.get('.route-search-progress progress').attributes('value')).toBe('100')
    const summary = wrapper.get('[aria-label="Route leg search status"]').text()
    expect(summary).toContain('DUB / SNN → AMS')
    expect(summary).toContain('3 fares found')
    expect(summary).toContain('AMS → CDG')
    expect(summary).toContain('No fares found')
    expect(wrapper.get('.connection-note--problem').text()).toContain('show where the route broke')
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
    expect(add.text()).toContain('Add another flight to this route')
    expect(add.text()).toContain('Continue from the current destination')
    expect(wrapper.get('.options-divider').text()).toBe('Trip and result options')
    await add.trigger('click')
    expect(wrapper.findAll('fieldset.ordered-leg')).toHaveLength(2)
    expect(add.attributes('disabled')).toBeDefined()
    expect(wrapper.get('.return-choice input').attributes('disabled')).toBeDefined()
    expect(wrapper.findAllComponents(OrderedLegEditor).every(editor => editor.props('maxAirports') === 1)).toBe(true)
    wrapper.unmount()
  })

  it('generates a dated final leg back to the starting airport group', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) => Promise.resolve(new Response(JSON.stringify(url.includes('/configuration')
      ? { providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 8 }
      : { searchId: 'ordered-return', mode: 'ordered', status: 'running', phase: 'searching', progress: 0, results: [], warnings: [] }
    ), { status: url.includes('/configuration') ? 200 : 202, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(OrderedRouteSearch)
    const selectedAirport = (code: string) => ({ code, name: code, displayLabel: code })
    wrapper.getComponent(OrderedLegEditor).vm.$emit('update:modelValue', {
      id: 'outbound', fromLabel: 'Home', toLabel: 'Netherlands', from: [selectedAirport('DUB')], to: [selectedAirport('AMS'), selectedAirport('EIN')], departureDate: '2026-09-01', continuity: 'sameAirport',
    })
    await wrapper.vm.$nextTick()
    await wrapper.get('.return-choice input').setValue(true)
    await wrapper.get('.return-details input[type="date"]').setValue('2026-09-05')

    expect(wrapper.get('[aria-label="Generated return leg"]').text()).toContain('AMS / EIN → DUB')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const postCall = fetchMock.mock.calls.find(([, options]) => options?.method === 'POST')!
    const request = JSON.parse(postCall[1].body)
    expect(request.legs).toHaveLength(2)
    expect(request.legs[1]).toMatchObject({
      id: 'ordered-return-to-start',
      departureDate: '2026-09-05',
      airportContinuityWithPrevious: 'sameAirport',
      from: { airportCodes: ['AMS', 'EIN'] },
      to: { airportCodes: ['DUB'] },
    })
    wrapper.unmount()
  })

  it('shows the simple-search spinner while an ordered route is running', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) => Promise.resolve(new Response(JSON.stringify(url.includes('/configuration')
      ? { providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 8 }
      : { searchId: 'ordered-running', mode: 'ordered', status: 'running', phase: 'searching', progress: 20, results: [], warnings: [] }
    ), { status: url.includes('/configuration') ? 200 : 202, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(OrderedRouteSearch)
    const editor = wrapper.getComponent(OrderedLegEditor)
    const selectedAirport = (code: string) => ({ code, name: code, displayLabel: code })
    editor.vm.$emit('update:modelValue', {
      id: 'one', fromLabel: 'Ireland', toLabel: 'Netherlands', from: [selectedAirport('DUB')], to: [selectedAirport('AMS')], departureDate: '2026-09-01', continuity: 'sameAirport',
    })
    await wrapper.vm.$nextTick()
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('.route-search-progress .progress-spinner').attributes('aria-hidden')).toBe('true')
    wrapper.unmount()
  })

  it('hydrates adjacent ordered legs without searching and consumes prefill after an edit', async () => {
    window.history.replaceState({}, '', '/build-route?route=DUB,AMS,JFK&prefill=true')
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 8,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(OrderedRouteSearch, { props: { prefillRoute: ['DUB', 'AMS', 'JFK'] } })
    await flushPromises()

    const editors = wrapper.findAllComponents(OrderedLegEditor)
    expect(editors).toHaveLength(2)
    expect(editors[0].props('modelValue')).toMatchObject({ from: [{ code: 'DUB' }], to: [{ code: 'AMS' }] })
    expect(editors[1].props('modelValue')).toMatchObject({ from: [{ code: 'AMS' }], to: [{ code: 'JFK' }] })
    const firstDate = Date.parse(`${editors[0].props('modelValue').departureDate}T00:00:00Z`)
    const secondDate = Date.parse(`${editors[1].props('modelValue').departureDate}T00:00:00Z`)
    expect(secondDate - firstDate).toBe(86_400_000)
    expect(fetchMock.mock.calls.some(([, options]) => options?.method === 'POST')).toBe(false)
    expect(window.location.search).toContain('prefill=true')

    editors[1].vm.$emit('update:modelValue', { ...editors[1].props('modelValue'), departureDate: '2026-09-20' })
    await wrapper.vm.$nextTick()
    expect(window.location.search).toBe('')
    wrapper.unmount()
  })

  it('resolves four-letter airport identifiers to named booking-code options', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) => {
      const body = url.includes('/configuration')
        ? { providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 8 }
        : url.includes('EIDW')
          ? { airports: [{ code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' }] }
          : { airports: [{ code: 'AMS', name: 'Amsterdam Airport Schiphol', displayLabel: 'Amsterdam Airport Schiphol (AMS)' }] }
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    })
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(OrderedRouteSearch, { props: { prefillRoute: ['EIDW', 'EHAM'] } })
    await flushPromises()

    expect(wrapper.getComponent(OrderedLegEditor).props('modelValue')).toMatchObject({
      from: [{ code: 'DUB', name: 'Dublin Airport' }],
      to: [{ code: 'AMS', name: 'Amsterdam Airport Schiphol' }],
    })
    expect(wrapper.text()).toContain('Dublin Airport (DUB)')
    expect(wrapper.text()).toContain('Amsterdam Airport Schiphol (AMS)')
    wrapper.unmount()
  })

  it('preserves route edits made while four-letter airport identifiers resolve', async () => {
    const pending = new Map<string, (response: Response) => void>()
    const fetchMock = vi.fn().mockImplementation((url: string) => {
      if (url.includes('/configuration')) return Promise.resolve(new Response(JSON.stringify({
        providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 8,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      const identifier = url.includes('EIDW') ? 'EIDW' : 'EHAM'
      return new Promise<Response>(resolve => pending.set(identifier, resolve))
    })
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(OrderedRouteSearch, { props: { prefillRoute: ['EIDW', 'EHAM'] } })
    await wrapper.vm.$nextTick()

    const editor = wrapper.getComponent(OrderedLegEditor)
    editor.vm.$emit('update:modelValue', { ...editor.props('modelValue'), departureDate: '2026-09-20' })
    await wrapper.vm.$nextTick()

    pending.get('EIDW')!(new Response(JSON.stringify({ airports: [{ code: 'DUB', name: 'Dublin Airport', displayLabel: 'Dublin Airport (DUB)' }] }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    pending.get('EHAM')!(new Response(JSON.stringify({ airports: [{ code: 'AMS', name: 'Amsterdam Airport Schiphol', displayLabel: 'Amsterdam Airport Schiphol (AMS)' }] }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    await flushPromises()

    expect(wrapper.getComponent(OrderedLegEditor).props('modelValue')).toMatchObject({
      departureDate: '2026-09-20',
      from: [{ code: 'DUB', name: 'Dublin Airport' }],
      to: [{ code: 'AMS', name: 'Amsterdam Airport Schiphol' }],
    })
    wrapper.unmount()
  })

  it('consumes an ordered prefill on submit and sends every generated adjacent leg', async () => {
    window.history.replaceState({}, '', '/build-route?route=DUB,AMS,JFK&prefill=true')
    const fetchMock = vi.fn().mockImplementation((url: string, options?: RequestInit) => Promise.resolve(new Response(JSON.stringify(url.includes('/configuration')
      ? { providerCallLimit: 25, maxOptimizedDestinations: 5, maxAirportsPerGroup: 5, maxTripDays: 31, maxOrderedLegs: 8 }
      : { searchId: 'prefilled-ordered', mode: 'ordered', status: 'completed', phase: 'completed', progress: 100, results: [], warnings: [], coverage: { mode: 'exhaustive', liveProviderCallsUsed: 2, providerCallLimit: 25, cacheHits: 0, candidatesEvaluated: 2, candidatesPruned: 0 }, orderedLegs: [] }
    ), { status: options?.method === 'POST' ? 202 : 200, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(OrderedRouteSearch, { props: { prefillRoute: ['DUB', 'AMS', 'JFK'] } })
    await flushPromises()

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const postCall = fetchMock.mock.calls.find(([, options]) => options?.method === 'POST')!
    const request = JSON.parse(postCall[1].body)
    expect(request.legs.map((leg: { from: { airportCodes: string[] }; to: { airportCodes: string[] } }) => [leg.from.airportCodes[0], leg.to.airportCodes[0]])).toEqual([['DUB', 'AMS'], ['AMS', 'JFK']])
    expect(window.location.search).toBe('')
    wrapper.unmount()
  })
})
