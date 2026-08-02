import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import MultiDestinationSearchPage from '../../../src/pages/MultiDestinationSearchPage.vue'
import AirportGroupPicker from '../../../src/components/flight-search/AirportGroupPicker.vue'

describe('MultiDestinationSearchPage', () => {
  it('uses the shared airport-group picker in both advanced modes', async () => {
    const wrapper = mount(MultiDestinationSearchPage)

    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(2)
    expect(wrapper.get('[aria-label="Build my route form"]').exists()).toBe(true)

    const tabs = wrapper.findAll('[role="tab"]')
    expect(tabs.map(tab => tab.text())).toEqual(['Build my route', 'Optimize my trip'])
    await tabs[1].trigger('click')

    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(2)
    expect(wrapper.get('[aria-label="Optimize my trip form"]').exists()).toBe(true)
  })

  it('exposes keyboard-operable tabs with associated tab panels', async () => {
    const wrapper = mount(MultiDestinationSearchPage)
    const ordered = wrapper.get('#multi-destination-tab-ordered')

    expect(ordered.attributes('aria-controls')).toBe('multi-destination-panel-ordered')
    expect(wrapper.get('[role="tabpanel"]').attributes('aria-labelledby')).toBe('multi-destination-tab-ordered')
    await ordered.trigger('keydown', { key: 'ArrowRight' })
    await wrapper.vm.$nextTick()

    expect(wrapper.get('#multi-destination-tab-optimize').attributes('aria-selected')).toBe('true')
    expect(wrapper.get('[role="tabpanel"]').attributes('aria-labelledby')).toBe('multi-destination-tab-optimize')
  })

  it('adds only one new airport picker for each connected ordered destination', async () => {
    const wrapper = mount(MultiDestinationSearchPage)
    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(2)
    await wrapper.get('button.secondary-action').trigger('click')
    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(3)
    expect(wrapper.findAll('fieldset.ordered-leg')).toHaveLength(2)

    await wrapper.get('[aria-label="Remove destination 2"]').trigger('click')
    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(2)
  })

  it('caps the ordered route builder at eight legs', async () => {
    const wrapper = mount(MultiDestinationSearchPage)
    const add = wrapper.get('button.secondary-action')

    for (let index = 1; index < 8; index += 1) await add.trigger('click')

    expect(wrapper.findAll('fieldset.ordered-leg')).toHaveLength(8)
    expect(add.attributes('disabled')).toBeDefined()
    await add.trigger('click')
    expect(wrapper.findAll('fieldset.ordered-leg')).toHaveLength(8)
  })
})
