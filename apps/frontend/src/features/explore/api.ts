import type { ExploreRoutesResponse } from './types'

const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim()
const apiBaseUrl = configuredApiBaseUrl ? configuredApiBaseUrl.replace(/\/$/, '') : ''

const readRoutes = async (response: Response): Promise<ExploreRoutesResponse> => {
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; errors?: Record<string, string[]> } | null
    const validation = problem?.errors ? Object.values(problem.errors).flat().join(' ') : ''
    throw new Error(validation || problem?.detail || `Could not load routes (HTTP ${response.status}).`)
  }
  return response.json() as Promise<ExploreRoutesResponse>
}

export const getExploreRoutes = async (origin: string, departureDate?: string, signal?: AbortSignal) => {
  const params = new URLSearchParams({ origin })
  if (departureDate) params.set('departureDate', departureDate)
  return readRoutes(await fetch(`${apiBaseUrl}/api/v1/explore/routes?${params}`, { credentials: 'include', signal }))
}

export const getHeroRoutes = async (signal?: AbortSignal) =>
  readRoutes(await fetch(`${apiBaseUrl}/api/v1/explore/hero`, { credentials: 'include', signal }))
