import type { LocationQueryValue } from 'vue-router'

export const getQueryString = (value: LocationQueryValue | LocationQueryValue[] | undefined) =>
  Array.isArray(value) ? value[0] ?? null : value ?? null

export const parseStringListParam = (value: string | null) =>
  (value ?? '').split(',').map((item) => item.trim()).filter(Boolean)

export const parseCodeListParam = (value: string | null) =>
  parseStringListParam(value).map((item) => item.toUpperCase())

export const parseDateListParam = (value: string | null) =>
  parseStringListParam(value).sort((left, right) => left.localeCompare(right))

export const parseNumberParam = (value: string | null, fallback: number) => {
  const parsed = value === null || value.trim() === '' ? Number.NaN : Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

export const parseBooleanParam = (value: string | null, fallback: boolean) =>
  value === '1' || value === 'true' ? true : value === '0' || value === 'false' ? false : fallback

export const parseRangeParam = (value: string | null, fallback: [number, number]): [number, number] => {
  if (!value) return fallback
  const [startRaw, endRaw] = value.split('-', 2)
  const start = Number(startRaw)
  const end = Number(endRaw)
  return Number.isFinite(start) && Number.isFinite(end) ? [Math.min(start, end), Math.max(start, end)] : fallback
}

export const buildSearchRequestKey = (
  origins: string[],
  destinations: string[],
  dates: string[],
  tripType: 'oneWay' | 'return',
  returnDates: string[],
  adults: number,
  cabinClass: string,
) => JSON.stringify({
  origins: [...origins].sort((left, right) => left.localeCompare(right)),
  destinations: [...destinations].sort((left, right) => left.localeCompare(right)),
  dates: [...dates].sort((left, right) => left.localeCompare(right)),
  tripType,
  returnDates: [...returnDates].sort((left, right) => left.localeCompare(right)),
  adults,
  cabinClass,
})

export const setListParam = (params: Record<string, string>, key: string, values: string[]) => {
  const cleanedValues = values.map((value) => value.trim()).filter(Boolean)
  if (cleanedValues.length > 0) params[key] = cleanedValues.join(',')
}

export const setBooleanParam = (params: Record<string, string>, key: string, value: boolean, fallback: boolean) => {
  if (value !== fallback) params[key] = value ? '1' : '0'
}

export const setNumberParam = (params: Record<string, string>, key: string, value: number, fallback: number) => {
  if (value !== fallback) params[key] = String(value)
}

export const setRangeParam = (params: Record<string, string>, key: string, value: [number, number], fallback: [number, number]) => {
  if (value[0] !== fallback[0] || value[1] !== fallback[1]) params[key] = `${value[0]}-${value[1]}`
}

export const getExplicitSelection = (selectedValues: string[], availableValues: string[]) => {
  const selected = selectedValues.map((value) => value.trim()).filter(Boolean)
  const available = availableValues.map((value) => value.trim()).filter(Boolean)

  if (
    selected.length === 0 ||
    (available.length > 0 &&
      selected.length === available.length &&
      available.every((value) => selected.includes(value)))
  ) {
    return []
  }

  return selected
}
