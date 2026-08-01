import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AirportGroupPicker from '../../../src/components/flight-search/AirportGroupPicker.vue'

describe('AirportGroupPicker', () => {
  it('supports keyboard selection and accessible removal', async () => {
    const airport = { code: 'DUB', name: 'Dublin', displayLabel: 'Dublin (DUB)' }
    const wrapper = mount(AirportGroupPicker, {
      props: {
        label: 'Airport group', inputAriaLabel: 'Add airport', suggestionsAriaLabel: 'Airport suggestions',
        suggestionIdPrefix: 'test', suggestions: [airport], input: 'du', airports: [],
        'onUpdate:input': () => {}, 'onUpdate:airports': () => {},
      },
    })
    const input = wrapper.get('[role="combobox"]')
    await input.trigger('keydown', { key: 'ArrowDown' })
    await input.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('addAirport')?.[0]?.[0]).toEqual(airport)

    await wrapper.setProps({ airports: [airport], suggestions: [] })
    expect(wrapper.get('.airport-chip').attributes('aria-label')).toContain('Remove Dublin')
    await wrapper.get('.airport-chip').trigger('click')
    expect(wrapper.emitted('removeAirport')?.[0]).toEqual(['DUB'])
  })

  it('enforces the configured airport-group limit', async () => {
    const selected = { code: 'DUB', name: 'Dublin', displayLabel: 'Dublin (DUB)' }
    const suggestion = { code: 'SNN', name: 'Shannon', displayLabel: 'Shannon (SNN)' }
    const wrapper = mount(AirportGroupPicker, {
      props: {
        label: 'Airport group', inputAriaLabel: 'Add airport', suggestionsAriaLabel: 'Airport suggestions',
        suggestionIdPrefix: 'limited', suggestions: [suggestion], input: '', airports: [selected], maxAirports: 1,
        'onUpdate:input': () => {}, 'onUpdate:airports': () => {},
      },
    })

    expect(wrapper.get('[role="combobox"]').attributes('disabled')).toBeDefined()
    await wrapper.get('.suggestion-button').trigger('click')
    expect(wrapper.emitted('addAirport')).toBeUndefined()
    expect(wrapper.text()).toContain('Up to 1 airport')
  })
})
