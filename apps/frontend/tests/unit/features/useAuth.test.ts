import { beforeEach, describe, expect, it, vi } from 'vitest'

const authApi = vi.hoisted(() => ({
  getCurrentUser: vi.fn(),
  login: vi.fn(),
  logout: vi.fn(),
  register: vi.fn(),
}))

vi.mock('../../../src/features/auth/api', () => authApi)

describe('useAuth', () => {
  beforeEach(() => {
    vi.resetModules()
    authApi.getCurrentUser.mockReset()
    authApi.login.mockReset()
    authApi.logout.mockReset()
    authApi.register.mockReset()
  })

  it('does not let a stale refresh overwrite a successful login', async () => {
    let resolveRefresh!: (user: { isAuthenticated: boolean; id: string | null; email: string | null; roles: string[] }) => void
    authApi.getCurrentUser.mockReturnValue(new Promise((resolve) => {
      resolveRefresh = resolve
    }))
    authApi.login.mockResolvedValue({
      isAuthenticated: true,
      id: 'user-1',
      email: 'luc@example.com',
      roles: ['User'],
    })

    const { useAuth } = await import('../../../src/features/auth/useAuth')
    const auth = useAuth()
    const refresh = auth.refresh()

    await auth.signIn({ email: 'luc@example.com', password: 'password123' })
    resolveRefresh({ isAuthenticated: false, id: null, email: null, roles: [] })
    await refresh

    expect(auth.user.value).toMatchObject({
      isAuthenticated: true,
      email: 'luc@example.com',
    })
  })
})
