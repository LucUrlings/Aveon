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
let authStateVersion = 0

export const useAuth = () => {
  const isAuthenticated = computed(() => user.value.isAuthenticated)

  const refresh = async () => {
    const refreshVersion = authStateVersion
    loading.value = true
    try {
      const refreshedUser = await getCurrentUser()
      if (refreshVersion === authStateVersion) {
        user.value = refreshedUser
      }
    } catch {
      if (refreshVersion === authStateVersion) {
        user.value = anonymousUser
      }
    } finally {
      if (refreshVersion === authStateVersion) {
        loading.value = false
      }
    }
  }

  const signIn = async (credentials: AuthCredentials) => {
    const mutationVersion = ++authStateVersion
    try {
      const signedInUser = await login(credentials)
      if (mutationVersion === authStateVersion) {
        user.value = signedInUser
      }
    } finally {
      if (mutationVersion === authStateVersion) {
        loading.value = false
      }
    }
  }

  const signUp = async (credentials: AuthCredentials) => {
    const mutationVersion = ++authStateVersion
    try {
      const registeredUser = await register(credentials)
      if (mutationVersion === authStateVersion) {
        user.value = registeredUser
      }
    } finally {
      if (mutationVersion === authStateVersion) {
        loading.value = false
      }
    }
  }

  const signOut = async () => {
    const mutationVersion = ++authStateVersion
    try {
      await logout()
      if (mutationVersion === authStateVersion) {
        user.value = anonymousUser
      }
    } finally {
      if (mutationVersion === authStateVersion) {
        loading.value = false
      }
    }
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
