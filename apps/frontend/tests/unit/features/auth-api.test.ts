import { afterEach, describe, expect, it, vi } from 'vitest'

const fetchMock = vi.fn()
vi.stubGlobal('fetch', fetchMock)

afterEach(() => {
  fetchMock.mockReset()
})

describe('auth api', () => {
  it('loads the current user with credentials', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({
        isAuthenticated: true,
        id: 'user-1',
        email: 'luc@example.com',
        roles: ['User'],
      }),
    })

    const { getCurrentUser } = await import('../../../src/features/auth/api')

    await expect(getCurrentUser()).resolves.toEqual({
      isAuthenticated: true,
      id: 'user-1',
      email: 'luc@example.com',
      roles: ['User'],
    })
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/auth/me'),
      expect.objectContaining({ credentials: 'include' }),
    )
  })

  it('posts login credentials with cookies enabled', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({
        isAuthenticated: true,
        id: 'user-1',
        email: 'luc@example.com',
        roles: ['User'],
      }),
    })

    const { login } = await import('../../../src/features/auth/api')

    await login({ email: 'luc@example.com', password: 'password123' })

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/auth/login'),
      expect.objectContaining({
        method: 'POST',
        credentials: 'include',
        body: JSON.stringify({ email: 'luc@example.com', password: 'password123' }),
      }),
    )
  })

  it('surfaces validation messages from problem details', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 400,
      headers: {
        get: (key: string) => key.toLowerCase() === 'content-type' ? 'application/problem+json' : null,
      },
      json: async () => ({
        errors: {
          Password: ['Password is too short.'],
        },
      }),
    })

    const { register } = await import('../../../src/features/auth/api')

    await expect(register({ email: 'luc@example.com', password: 'short' })).rejects.toThrow('Password is too short.')
  })

})
