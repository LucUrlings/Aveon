import type { ItineraryResultsQuery, ItinerarySearchCapabilities, ItinerarySearchRequest, ItinerarySearchSession } from './types'

const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim()
const apiBaseUrl = configuredApiBaseUrl ? configuredApiBaseUrl.replace(/\/$/, '') : import.meta.env.DEV ? 'http://localhost:5210' : ''

const readResponse = async (response: Response): Promise<ItinerarySearchSession> => {
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; errors?: Record<string, string[]> } | null
    const validation = problem?.errors ? Object.values(problem.errors).flat().join(' ') : ''
    throw new Error(validation || problem?.detail || `HTTP ${response.status}`)
  }
  return response.json() as Promise<ItinerarySearchSession>
}

export const startItinerarySearch = async (request: ItinerarySearchRequest, signal?: AbortSignal) =>
  readResponse(await fetch(`${apiBaseUrl}/api/v1/itinerary-searches`, {
    method: 'POST', credentials: 'include', signal,
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request),
  }))

const queryString = (query: ItineraryResultsQuery) => {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || (typeof value === 'string' && value.length === 0)) continue
    const separator = key === 'departureTime' || key === 'arrivalTime' ? '-' : ','
    params.set(key, Array.isArray(value) ? value.join(separator) : String(value))
  }
  const encoded = params.toString()
  return encoded ? `?${encoded}` : ''
}

export const getItinerarySearch = async (searchId: string, query: ItineraryResultsQuery = {}, signal?: AbortSignal) =>
  readResponse(await fetch(`${apiBaseUrl}/api/v1/itinerary-searches/${encodeURIComponent(searchId)}${queryString(query)}`, { credentials: 'include', signal }))

export const getItinerarySearchCapabilities = async (signal?: AbortSignal): Promise<ItinerarySearchCapabilities> => {
  const response = await fetch(`${apiBaseUrl}/api/v1/itinerary-searches/configuration`, { credentials: 'include', signal })
  if (!response.ok) throw new Error(`HTTP ${response.status}`)
  return response.json() as Promise<ItinerarySearchCapabilities>
}

export const cancelItinerarySearch = async (searchId: string, signal?: AbortSignal) => {
  const response = await fetch(`${apiBaseUrl}/api/v1/itinerary-searches/${encodeURIComponent(searchId)}`, { method: 'DELETE', credentials: 'include', signal })
  if (!response.ok && response.status !== 404) throw new Error(`HTTP ${response.status}`)
}
