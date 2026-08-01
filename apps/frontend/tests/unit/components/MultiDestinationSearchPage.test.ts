import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import MultiDestinationSearchPage from '../../../src/pages/MultiDestinationSearchPage.vue'
import AirportGroupPicker from '../../../src/components/flight-search/AirportGroupPicker.vue'

describe('MultiDestinationSearchPage', () => {
  it('uses the shared airport-group picker in both advanced modes', async () => {
    const wrapper = mount(MultiDestinationSearchPage)

    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(2)
    expect(wrapper.get('[aria-label="Optimize my trip form"]').exists()).toBe(true)

    await wrapper.findAll('[role="tab"]')[1].trigger('click')

    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(2)
    expect(wrapper.get('[aria-label="Build my route form"]').exists()).toBe(true)
  })

  it('uses two shared airport-group pickers for every dynamic ordered leg', async () => {
    const wrapper = mount(MultiDestinationSearchPage)
    await wrapper.findAll('[role="tab"]')[1].trigger('click')

    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(2)
    await wrapper.get('button.secondary-action').trigger('click')
    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(4)
    expect(wrapper.findAll('fieldset.ordered-leg')).toHaveLength(2)

    await wrapper.get('[aria-label="Remove flight 2"]').trigger('click')
    expect(wrapper.findAllComponents(AirportGroupPicker)).toHaveLength(2)
  })
})
