import { describe, expect, it } from 'vitest'
import {
  buildSearchRequestKey,
  getExplicitSelection,
  parseBooleanParam,
  parseCodeListParam,
  parseDateListParam,
  parseNumberParam,
  parseRangeParam,
  setBooleanParam,
  setListParam,
  setNumberParam,
  setRangeParam,
} from '../../../src/features/flight-search/searchRoute'

describe('searchRoute', () => {
  it('normalizes list, date, number, boolean, and range query values', () => {
    expect(parseCodeListParam(' dub,ams, ')).toEqual(['DUB', 'AMS'])
    expect(parseDateListParam('2026-08-12,2026-08-07')).toEqual(['2026-08-07', '2026-08-12'])
    expect(parseNumberParam('not-a-number', 3)).toBe(3)
    expect(parseBooleanParam('false', true)).toBe(false)
    expect(parseRangeParam('1200-300', [0, 1439])).toEqual([300, 1200])
  })

  it('builds stable search keys regardless of selection order', () => {
    const first = buildSearchRequestKey(
      ['DUB', 'SNN'], ['AMS', 'CGN'], ['2026-08-08', '2026-08-07'],
      'return', ['2026-08-12', '2026-08-11'], 1, 'economy',
    )
    const second = buildSearchRequestKey(
      ['SNN', 'DUB'], ['CGN', 'AMS'], ['2026-08-07', '2026-08-08'],
      'return', ['2026-08-11', '2026-08-12'], 1, 'economy',
    )

    expect(first).toBe(second)
  })

  it('omits defaults and selections that already represent every available option', () => {
    const query: Record<string, string> = {}
    setListParam(query, 'providers', getExplicitSelection(['KLM', 'Ryanair'], ['KLM', 'Ryanair']))
    setListParam(query, 'airlines', getExplicitSelection(['KLM'], ['KLM', 'Ryanair']))
    setBooleanParam(query, 'direct', true, true)
    setBooleanParam(query, 'oneStop', true, false)
    setNumberParam(query, 'maxDuration', 600, 600)
    setRangeParam(query, 'departureTime', [300, 900], [0, 1439])

    expect(query).toEqual({
      airlines: 'KLM',
      oneStop: '1',
      departureTime: '300-900',
    })
  })
})
