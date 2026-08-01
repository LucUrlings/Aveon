import { onBeforeUnmount, ref, watch } from 'vue'
import { fetchAirportSuggestions } from './api'
import type { AirportOption } from './types'

const isAbortError = (error: unknown) =>
  error instanceof Error && error.name === 'AbortError'

export const useAirportPicker = (initialAirports: AirportOption[]) => {
  const input = ref('')
  const airports = ref<AirportOption[]>([...initialAirports])
  const suggestions = ref<AirportOption[]>([])
  const suggestionsLoading = ref(false)
  const suggestionsError = ref<string | null>(null)
  const hasSearchedSuggestions = ref(false)
  let requestId = 0
  let timer: number | null = null
  let controller: AbortController | null = null

  const addAirport = (airport: AirportOption) => {
    if (airports.value.some((item) => item.code === airport.code)) {
      input.value = ''
      suggestions.value = []
      return
    }

    airports.value = [...airports.value, airport]
    input.value = ''
    suggestions.value = []
  }

  const removeAirport = (code: string) => {
    airports.value = airports.value.filter((airport) => airport.code !== code)
  }

  const confirmInput = () => {
    const value = input.value.trim().toLowerCase()
    if (!value) {
      return
    }

    const match = suggestions.value.find((airport) =>
      airport.code.toLowerCase() === value || airport.displayLabel.toLowerCase() === value)
    if (match) {
      addAirport(match)
    }
  }

  watch(input, (query) => {
    requestId += 1
    const currentRequestId = requestId
    if (timer !== null) {
      window.clearTimeout(timer)
    }
    controller?.abort()
    suggestionsError.value = null
    hasSearchedSuggestions.value = false

    const trimmed = query.trim()
    if (trimmed.length < 2) {
      suggestions.value = []
      suggestionsLoading.value = false
      return
    }

    suggestions.value = []
    suggestionsLoading.value = true
    controller = new AbortController()
    const activeController = controller
    timer = window.setTimeout(async () => {
      timer = null
      try {
        const matches = await fetchAirportSuggestions(trimmed, activeController.signal)
        if (currentRequestId === requestId) {
          suggestions.value = matches.filter((airport) =>
            !airports.value.some((item) => item.code === airport.code))
          hasSearchedSuggestions.value = true
        }
      } catch (error) {
        if (currentRequestId === requestId && !isAbortError(error)) {
          suggestions.value = []
          suggestionsError.value = 'Airport suggestions are unavailable. Try again.'
          hasSearchedSuggestions.value = true
        }
      } finally {
        if (currentRequestId === requestId) {
          suggestionsLoading.value = false
        }
      }
    }, 200)
  })

  onBeforeUnmount(() => {
    if (timer !== null) {
      window.clearTimeout(timer)
    }
    controller?.abort()
  })

  return {
    input,
    airports,
    suggestions,
    suggestionsLoading,
    suggestionsError,
    hasSearchedSuggestions,
    addAirport,
    removeAirport,
    confirmInput,
  }
}
