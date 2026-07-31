<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { updatePreferences } from '../features/auth/api'
import { useAuth } from '../features/auth/useAuth'
import { returnRankingOptions, useSearchPreferences, type ReturnRanking } from '../features/preferences/useSearchPreferences'

const auth = useAuth()
const preferences = useSearchPreferences()
const authMode = ref<'login' | 'register'>('login')
const authEmail = ref('')
const authPassword = ref('')
const authError = ref<string | null>(null)
const authSubmitting = ref(false)
const preferencesError = ref<string | null>(null)
const preferencesSaving = ref(false)
const accountMenu = ref<HTMLDetailsElement | null>(null)

onMounted(async () => {
  await auth.refresh()
  preferences.applyAccountReturnRanking(auth.user.value.defaultReturnRanking)
})

const submitAuth = async () => {
  authError.value = null
  authSubmitting.value = true
  try {
    const credentials = { email: authEmail.value.trim(), password: authPassword.value }
    if (authMode.value === 'register') await auth.signUp(credentials)
    else await auth.signIn(credentials)
    authPassword.value = ''
    preferences.applyAccountReturnRanking(auth.user.value.defaultReturnRanking)
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

const updateReturnRanking = async (value: ReturnRanking) => {
  preferencesError.value = null
  preferences.setReturnRanking(value)

  if (!auth.isAuthenticated.value) return

  preferencesSaving.value = true
  try {
    const user = await updatePreferences(value)
    preferences.applyAccountReturnRanking(user.defaultReturnRanking)
  } catch (error) {
    preferencesError.value = error instanceof Error
      ? `${error.message} Your choice is saved only on this device.`
      : 'Could not save to your account. Your choice is saved only on this device.'
  } finally {
    preferencesSaving.value = false
  }
}
</script>

<template>
  <header class="site-header">
    <nav class="navbar" aria-label="Main navigation">
      <RouterLink class="navbar-brand" to="/" aria-label="Aveon flight search">
        <span class="navbar-logo" aria-hidden="true">
          <svg viewBox="0 0 24 24"><path d="M4 15.5 20 5l-5.3 14-3.1-5.2L4 15.5Z" /></svg>
        </span>
        <span class="navbar-wordmark">Aveon</span>
        <span class="navbar-product">Flight discovery</span>
      </RouterLink>

      <div class="navbar-links">
        <RouterLink to="/" exact-active-class="active">Search</RouterLink>
        <RouterLink to="/how-it-works" active-class="active">How it works</RouterLink>
        <RouterLink to="/about" active-class="active">About</RouterLink>
        <a href="https://github.com/LucUrlings/Aveon" target="_blank" rel="noreferrer">Source <span aria-hidden="true">↗</span></a>
      </div>

      <div class="navbar-actions">
        <details class="preferences-menu">
          <summary aria-label="Search preferences">
            <span aria-hidden="true">⚙</span>
            <span class="preferences-label">Preferences</span>
          </summary>
          <div class="preferences-popover">
            <p class="account-kicker">Return options</p>
            <strong>Default ranking</strong>
            <p class="preferences-copy">
              Used whenever you choose an outbound flight.
              {{ auth.isAuthenticated.value ? 'Saved to your account.' : 'Saved on this device until you sign in.' }}
            </p>
            <div class="ranking-options" role="radiogroup" aria-label="Default return ranking">
              <button
                v-for="option in returnRankingOptions"
                :key="option.value"
                type="button"
                :class="{ active: preferences.returnRanking.value === option.value }"
                role="radio"
                :aria-checked="preferences.returnRanking.value === option.value"
                :disabled="preferencesSaving"
                @click="updateReturnRanking(option.value)"
              >
                <span><strong>{{ option.label }}</strong><small>{{ option.description }}</small></span>
                <span class="ranking-check" aria-hidden="true">✓</span>
              </button>
            </div>
            <p v-if="preferencesError" class="account-error" role="alert">{{ preferencesError }}</p>
          </div>
        </details>

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

          <form v-else @submit.prevent="submitAuth">
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
      </div>
    </nav>
  </header>
</template>

<style scoped src="./AppNavbar.css"></style>
