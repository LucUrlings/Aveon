import type { AuthCredentials, CurrentUser } from './types'

type ApiCurrentUser = {
  isAuthenticated?: boolean | null
  id?: string | null
  email?: string | null
  roles?: string[] | null
}

const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim()
const apiBaseUrl = configuredApiBaseUrl ? configuredApiBaseUrl.replace(/\/$/, '') : ''

const normalizeCurrentUser = (user: ApiCurrentUser): CurrentUser => ({
  isAuthenticated: user.isAuthenticated ?? false,
  id: user.id ?? null,
  email: user.email ?? null,
  roles: user.roles ?? [],
})

const readErrorMessage = async (response: Response) => {
  const fallback = `HTTP ${response.status}`
  const contentType = response.headers.get('content-type') ?? ''

  if (contentType.includes('application/json') || contentType.includes('+json')) {
    const problem = await response.json().catch(() => null) as {
      title?: string
      detail?: string
      errors?: Record<string, string[]>
    } | null

    const validationMessage = problem?.errors
      ? Object.values(problem.errors).flat().filter(Boolean).join(' ')
      : ''

    return validationMessage || problem?.detail || problem?.title || fallback
  }

  const message = await response.text()
  return message || fallback
}

const requestCurrentUser = async (path: string, init?: RequestInit) => {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    credentials: 'include',
    headers: {
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response))
  }

  return normalizeCurrentUser((await response.json()) as ApiCurrentUser)
}

export const getCurrentUser = () =>
  requestCurrentUser('/api/v1/auth/me')

export const register = (credentials: AuthCredentials) =>
  requestCurrentUser('/api/v1/auth/register', {
    method: 'POST',
    body: JSON.stringify(credentials),
  })

export const login = (credentials: AuthCredentials) =>
  requestCurrentUser('/api/v1/auth/login', {
    method: 'POST',
    body: JSON.stringify(credentials),
  })

export const logout = async () => {
  const response = await fetch(`${apiBaseUrl}/api/v1/auth/logout`, {
    method: 'POST',
    credentials: 'include',
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response))
  }
}
