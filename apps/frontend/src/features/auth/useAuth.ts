import { computed, readonly, ref } from 'vue'
import { getCurrentUser, login, logout, register } from './api'
import type { AuthCredentials, CurrentUser } from './types'

const anonymousUser: CurrentUser = {
  isAuthenticated: false,
  id: null,
  email: null,
  roles: [],
}

const user = ref<CurrentUser>(anonymousUser)
const loading = ref(false)

export const useAuth = () => {
  const isAuthenticated = computed(() => user.value.isAuthenticated)

  const refresh = async () => {
    loading.value = true
    try {
      user.value = await getCurrentUser()
    } catch {
      user.value = anonymousUser
    } finally {
      loading.value = false
    }
  }

  const signIn = async (credentials: AuthCredentials) => {
    user.value = await login(credentials)
  }

  const signUp = async (credentials: AuthCredentials) => {
    user.value = await register(credentials)
  }

  const signOut = async () => {
    await logout()
    user.value = anonymousUser
  }

  return {
    user: readonly(user),
    loading: readonly(loading),
    isAuthenticated,
    refresh,
    signIn,
    signUp,
    signOut,
  }
}
