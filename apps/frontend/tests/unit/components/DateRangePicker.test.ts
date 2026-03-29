import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import DateRangePicker from '../../../src/components/flight-search/DateRangePicker.vue'

const mountPicker = (overrides: Record<string, unknown> = {}) =>
  mount(DateRangePicker, {
    props: {
      maxRangeDays: 4,
      startDate: '2026-05-15',
      endDate: '2026-05-17',
      selectedDates: ['2026-05-15', '2026-05-16', '2026-05-17'],
      ...overrides,
    },
  })

describe('DateRangePicker', () => {
  it('shows all selected dates in the trigger label', () => {
    const wrapper = mountPicker()

    expect(wrapper.text()).toContain('15 May 2026')
    expect(wrapper.text()).toContain('16 May 2026')
    expect(wrapper.text()).toContain('17 May 2026')
  })

  it('switches to specific dates mode', async () => {
    const wrapper = mountPicker()

    await wrapper.get('.date-range-trigger').trigger('click')
    await wrapper.get('.selection-mode-button:last-of-type').trigger('click')

    expect(wrapper.text()).toContain('Specific dates mode')
    expect(wrapper.text()).toContain('Up to 4 picked dates')
  })

  it('emits the full consecutive date list in range mode', async () => {
    const wrapper = mountPicker({
      startDate: '2026-05-15',
      endDate: '2026-05-18',
      selectedDates: [],
    })

    const selectedDatesUpdates = wrapper.emitted('update:selectedDates')

    expect(selectedDatesUpdates).toBeTruthy()
    expect(selectedDatesUpdates?.at(-1)?.[0]).toEqual([
      '2026-05-15',
      '2026-05-16',
      '2026-05-17',
      '2026-05-18',
    ])
  })

  it('lets users toggle non-consecutive dates in specific mode', async () => {
    const wrapper = mountPicker({
      selectedDates: ['2026-05-15', '2026-05-17'],
    })

    await wrapper.get('.date-range-trigger').trigger('click')
    await wrapper.get('.selection-mode-button:last-of-type').trigger('click')
    await wrapper.findAll('.calendar-day').find((day) => day.text() === '16')?.trigger('click')

    const selectedDatesUpdates = wrapper.emitted('update:selectedDates')

    expect(selectedDatesUpdates?.some((event) => JSON.stringify(event[0]) === JSON.stringify([
      '2026-05-15',
      '2026-05-16',
      '2026-05-17',
    ]))).toBe(true)
  })
})
