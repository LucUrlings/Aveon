<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useAuth } from '../features/auth/useAuth'

const auth = useAuth()
const authMode = ref<'login' | 'register'>('login')
const authEmail = ref('')
const authPassword = ref('')
const authError = ref<string | null>(null)
const authSubmitting = ref(false)
const accountMenu = ref<HTMLDetailsElement | null>(null)

onMounted(() => void auth.refresh())

const submitAuth = async () => {
  authError.value = null
  authSubmitting.value = true
  try {
    const credentials = { email: authEmail.value.trim(), password: authPassword.value }
    if (authMode.value === 'register') await auth.signUp(credentials)
    else await auth.signIn(credentials)
    authPassword.value = ''
    if (accountMenu.value) accountMenu.value.open = false
  } catch (error) {
    authError.value = error instanceof Error ? error.message : 'Authentication failed.'
  } finally {
    authSubmitting.value = false
  }
}

const signOut = async () => {
  authError.value = null
  authSubmitting.value = true
  try {
    await auth.signOut()
    if (accountMenu.value) accountMenu.value.open = false
  } catch (error) {
    authError.value = error instanceof Error ? error.message : 'Logout failed.'
  } finally {
    authSubmitting.value = false
  }
}

</script>

<template>
  <header class="site-header">
    <nav class="navbar" aria-label="Main navigation">
      <RouterLink class="navbar-brand" to="/" aria-label="Aveon home">
        <span class="navbar-logo" aria-hidden="true">
          <svg viewBox="0 0 24 24"><path d="M4 15.5 20 5l-5.3 14-3.1-5.2L4 15.5Z" /></svg>
        </span>
        <span class="navbar-wordmark">Aveon</span>
        <span class="navbar-product">Flight discovery</span>
      </RouterLink>

      <div class="navbar-links">
        <RouterLink to="/" exact-active-class="active">Home</RouterLink>
        <RouterLink to="/search" active-class="active">Search</RouterLink>
        <RouterLink to="/multi-destination" active-class="active">Multi-destination</RouterLink>
        <RouterLink to="/how-it-works" active-class="active">How it works</RouterLink>
        <RouterLink to="/about" active-class="active">About</RouterLink>
        <a href="https://github.com/LucUrlings/Aveon" target="_blank" rel="noreferrer">Source <span aria-hidden="true">↗</span></a>
      </div>

      <details ref="accountMenu" class="account-menu">
        <summary>
          <span class="account-avatar" aria-hidden="true">{{ auth.isAuthenticated.value ? '✓' : '○' }}</span>
          <span>{{ auth.isAuthenticated.value ? 'Account' : 'Sign in' }}</span>
        </summary>

        <div class="account-popover">
          <template v-if="auth.isAuthenticated.value">
            <p class="account-kicker">Signed in as</p>
            <strong>{{ auth.user.value.email }}</strong>
            <button class="account-secondary-button" type="button" :disabled="authSubmitting" @click="signOut">
              {{ authSubmitting ? 'Signing out…' : 'Sign out' }}
            </button>
          </template>

          <form v-else :aria-busy="authSubmitting" @submit.prevent="submitAuth">
            <div>
              <p class="account-kicker">Your Aveon account</p>
              <strong>{{ authMode === 'login' ? 'Welcome back' : 'Create an account' }}</strong>
            </div>
            <label><span>Email</span><input v-model="authEmail" type="email" autocomplete="email" required /></label>
            <label>
              <span>Password</span>
              <input v-model="authPassword" type="password" :autocomplete="authMode === 'register' ? 'new-password' : 'current-password'" required />
            </label>
            <p v-if="authError" class="account-error" role="alert">{{ authError }}</p>
            <button class="account-primary-button" type="submit" :disabled="authSubmitting">
              {{ authSubmitting ? 'Working…' : authMode === 'login' ? 'Sign in' : 'Register' }}
            </button>
            <button class="account-mode-button" type="button" @click="authMode = authMode === 'login' ? 'register' : 'login'">
              {{ authMode === 'login' ? 'New here? Create an account' : 'Already registered? Sign in' }}
            </button>
          </form>
        </div>
      </details>
    </nav>
  </header>
</template>

<style scoped src="./AppNavbar.css"></style>
