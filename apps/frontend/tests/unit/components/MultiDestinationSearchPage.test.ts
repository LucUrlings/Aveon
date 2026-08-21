import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import AirportGroupPicker from '../../../src/components/flight-search/AirportGroupPicker.vue'
import BuildRoutePage from '../../../src/pages/BuildRoutePage.vue'
import OptimizeTripPage from '../../../src/pages/OptimizeTripPage.vue'

const { routeQuery } = vi.hoisted(() => ({ routeQuery: {} as Record<string, string> }))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: routeQuery }) }))

describe('multi-destination pages', () => {
  it('gives the ordered builder and optimizer separate pages without mode tabs', () => {
    const builder = mount(BuildRoutePage)
    const optimizer = mount(OptimizeTripPage)

    expect(builder.get('h1').text()).toContain('exact multi-destination route')
    expect(builder.get('[aria-label="Build my route form"]').exists()).toBe(true)
    expect(optimizer.get('h1').text()).toContain('Compare complete multi-destination journeys')
    expect(optimizer.get('[aria-label="Optimize my trip form"]').exists()).toBe(true)
    expect(builder.find('[role="tablist"]').exists()).toBe(false)
    expect(optimizer.find('[role="tablist"]').exists()).toBe(false)
    expect(builder.findAllComponents(AirportGroupPicker)).toHaveLength(2)
    expect(optimizer.findAllComponents(AirportGroupPicker)).toHaveLength(2)
  })

  it('adds only one new airport picker for each connected ordered destination', async () => {
    const wrapper = mount(BuildRoutePage)
    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(2)
    await wrapper.get('button.secondary-action').trigger('click')
    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(3)
    expect(wrapper.findAll('fieldset.ordered-leg')).toHaveLength(2)

    await wrapper.get('[aria-label="Remove destination 2"]').trigger('click')
    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(2)
  })

  it('caps the ordered route builder at eight legs', async () => {
    const wrapper = mount(BuildRoutePage)
    const add = wrapper.get('button.secondary-action')

    for (let index = 1; index < 8; index += 1) await add.trigger('click')

    expect(wrapper.findAll('fieldset.ordered-leg')).toHaveLength(8)
    expect(add.attributes('disabled')).toBeDefined()
    await add.trigger('click')
    expect(wrapper.findAll('fieldset.ordered-leg')).toHaveLength(8)
  })

  it('opens the route builder with an adjacent-leg route prefill', () => {
    Object.assign(routeQuery, { route: 'DUB,AMS,JFK', prefill: 'true' })
    const wrapper = mount(BuildRoutePage)

    const editors = wrapper.findAll('fieldset.ordered-leg')
    expect(editors).toHaveLength(2)
    expect(wrapper.text()).toContain('DUB')
    expect(wrapper.text()).toContain('JFK')
    delete routeQuery.route; delete routeQuery.prefill
  })

  it('warns an Explore handoff that onward dates and fares still need validation', () => {
    Object.assign(routeQuery, { route: 'DUB,AMS,JFK', departureDate: '2026-09-18', source: 'explore', prefill: 'true' })
    const wrapper = mount(BuildRoutePage)

    const editors = wrapper.findAll('fieldset.ordered-leg')
    expect(wrapper.get('.explore-handoff-note').text()).toContain('may not operate or return fares')
    expect(editors[0].get('input[type="date"]').element).toHaveProperty('value', '2026-09-18')
    expect(editors[1].get('input[type="date"]').element).toHaveProperty('value', '')
    for (const key of ['route', 'departureDate', 'source', 'prefill']) delete routeQuery[key]
  })
})
