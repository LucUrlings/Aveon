import { describe, expect, it, vi } from 'vitest'
import { localDateWithOffset, toLocalDateInputValue } from '../../../src/features/explore/localDate'

describe('Explore local date formatting', () => {
  it('formats the browser-local calendar date without converting through UTC', () => {
    const date = new Date('2026-08-08T22:30:00.000Z')
    vi.spyOn(date, 'getFullYear').mockReturnValue(2026)
    vi.spyOn(date, 'getMonth').mockReturnValue(7)
    vi.spyOn(date, 'getDate').mockReturnValue(9)

    expect(date.toISOString().slice(0, 10)).toBe('2026-08-08')
    expect(toLocalDateInputValue(date)).toBe('2026-08-09')
  })

  it('applies offsets with local calendar arithmetic across month boundaries', () => {
    const localEndOfMonth = new Date(2026, 7, 31, 23, 45)

    expect(localDateWithOffset(1, localEndOfMonth)).toBe('2026-09-01')
  })
})
