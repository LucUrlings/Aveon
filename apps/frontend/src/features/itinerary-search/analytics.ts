export type ItineraryAnalyticsEvent =
  | 'form_abandonment'
  | 'validation_failure'
  | 'completed_search'
  | 'bounded_coverage'
  | 'result_selection'
  | 'booking_click'

type AnalyticsValue = string | number | boolean

declare global {
  interface Window {
    umami?: { track: (event: string, properties?: Record<string, AnalyticsValue>) => void }
  }
}

const allowedProperties: Record<ItineraryAnalyticsEvent, readonly string[]> = {
  form_abandonment: ['mode'],
  validation_failure: ['mode', 'stage'],
  completed_search: ['mode', 'status', 'coverage', 'result_count'],
  bounded_coverage: ['mode', 'provider_call_limit', 'live_provider_calls'],
  result_selection: ['ranking', 'booking_type'],
  booking_click: ['booking_type', 'booking_count', 'position'],
}

export const trackItineraryEvent = (event: ItineraryAnalyticsEvent, properties: Record<string, AnalyticsValue> = {}) => {
  const allowed = new Set(allowedProperties[event])
  const safeProperties = Object.fromEntries(Object.entries(properties).filter(([key]) => allowed.has(key)))
  window.umami?.track(event, safeProperties)
}
