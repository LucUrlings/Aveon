import { computed, ref } from 'vue'
import { getSearchSession, searchFlightsRequest } from './api'
import type { SearchRequest, SearchResponse, SearchResult, SearchResultsQuery, SearchSessionResponse } from './types'

const isAbortError = (error: unknown) =>
  error instanceof Error && error.name === 'AbortError'

const mergeResultsById = (existing: SearchResult[], incoming: SearchResult[]) => {
  const results = [...existing]
  const indexes = new Map(results.map((result, index) => [result.id, index]))

  for (const result of incoming) {
    const existingIndex = indexes.get(result.id)
    if (existingIndex === undefined) {
      indexes.set(result.id, results.length)
      results.push(result)
    } else {
      results[existingIndex] = result
    }
  }

  return results
}

type SearchSessionOptions = {
  buildQuery: () => SearchResultsQuery
  buildRequest: () => SearchRequest
  getSearchKey: () => string | null
  validateRequest: () => string | null
  isReady: () => boolean
  onSearchReset: () => void
  onSearchAccepted: () => void
}

export const useSearchSession = (options: SearchSessionOptions) => {
  const loading = ref(false)
  const error = ref<string | null>(null)
  const response = ref<SearchResponse | null>(null)
  const searchSession = ref<SearchSessionResponse | null>(null)
  const loadedResults = ref<SearchResult[]>([])
  const currentPage = ref(1)
  const isLoadingMore = ref(false)
  const lastExecutedSearchKey = ref<string | null>(null)
  const isPolling = computed(() => searchSession.value?.status === 'running')
  const totalPages = computed(() => response.value?.pagination.totalPages ?? 0)
  const hasMoreResults = computed(() => response.value !== null && currentPage.value < totalPages.value)

  let searchRequestController: AbortController | null = null
  let searchSessionController: AbortController | null = null
  let pollingTimer: number | null = null
  let filterRefreshTimer: number | null = null
  let activeSearchGeneration = 0
  let latestSessionRequestId = 0

  const stopPolling = () => {
    if (pollingTimer !== null) {
      window.clearTimeout(pollingTimer)
      pollingTimer = null
    }
  }

  const loadSearchSession = async (
    searchId: string,
    loadOptions: { page?: number; append?: boolean; generation?: number } = {},
  ) => {
    const page = loadOptions.page ?? currentPage.value
    const append = loadOptions.append ?? false
    const generation = loadOptions.generation ?? activeSearchGeneration
    const requestId = ++latestSessionRequestId
    searchSessionController?.abort()
    const controller = new AbortController()
    searchSessionController = controller

    let session: SearchSessionResponse
    try {
      session = await getSearchSession(searchId, {
        ...options.buildQuery(),
        page,
      }, controller.signal)
    } finally {
      if (searchSessionController === controller) {
        searchSessionController = null
      }
    }

    if (generation !== activeSearchGeneration || requestId !== latestSessionRequestId) {
      return null
    }

    searchSession.value = session
    loadedResults.value = append
      ? mergeResultsById(loadedResults.value, session.response.results)
      : [...session.response.results]
    response.value = { ...session.response, results: [...loadedResults.value] }
    error.value = session.errorMessage ?? null
    currentPage.value = page
    return session
  }

  const pollSearchSession = async (searchId: string, generation: number): Promise<void> => {
    try {
      const session = await loadSearchSession(searchId, { page: 1, append: false, generation })
      if (!session || generation !== activeSearchGeneration) return

      if (session.status === 'running') {
        pollingTimer = window.setTimeout(() => void pollSearchSession(searchId, generation), 1000)
      } else {
        stopPolling()
      }
    } catch (cause) {
      if (generation === activeSearchGeneration && !isAbortError(cause)) {
        error.value = cause instanceof Error ? cause.message : 'Unknown error'
        stopPolling()
      }
    }
  }

  const scheduleRefresh = () => {
    if (!options.isReady() || !searchSession.value?.searchId) return
    if (filterRefreshTimer !== null) window.clearTimeout(filterRefreshTimer)

    const searchId = searchSession.value.searchId
    const generation = activeSearchGeneration
    filterRefreshTimer = window.setTimeout(async () => {
      filterRefreshTimer = null
      try {
        const session = await loadSearchSession(searchId, { page: 1, append: false, generation })
        if (session?.status === 'running' && generation === activeSearchGeneration) {
          stopPolling()
          pollingTimer = window.setTimeout(() => void pollSearchSession(searchId, generation), 1000)
        }
      } catch (cause) {
        if (generation === activeSearchGeneration && !isAbortError(cause)) {
          error.value = cause instanceof Error ? cause.message : 'Unknown error'
        }
      }
    }, 200)
  }

  const search = async () => {
    const validationError = options.validateRequest()
    if (validationError) {
      error.value = validationError
      return
    }

    const generation = ++activeSearchGeneration
    latestSessionRequestId += 1
    searchRequestController?.abort()
    searchSessionController?.abort()
    const controller = new AbortController()
    searchRequestController = controller
    stopPolling()
    if (filterRefreshTimer !== null) {
      window.clearTimeout(filterRefreshTimer)
      filterRefreshTimer = null
    }

    loading.value = true
    error.value = null
    response.value = null
    searchSession.value = null
    loadedResults.value = []
    currentPage.value = 1
    options.onSearchReset()

    try {
      lastExecutedSearchKey.value = options.getSearchKey()
      const session = await searchFlightsRequest(options.buildRequest(), controller.signal)
      if (generation !== activeSearchGeneration) return

      searchSession.value = session
      loadedResults.value = [...session.response.results]
      response.value = { ...session.response, results: [...loadedResults.value] }
      loading.value = false
      options.onSearchAccepted()

      if (session.status === 'running') {
        await pollSearchSession(session.searchId, generation)
      } else {
        await loadSearchSession(session.searchId, { page: 1, append: false, generation })
      }
    } catch (cause) {
      if (generation === activeSearchGeneration && !isAbortError(cause)) {
        error.value = cause instanceof Error ? cause.message : 'Unknown error'
      }
    } finally {
      if (searchRequestController === controller) searchRequestController = null
      if (generation === activeSearchGeneration && !isPolling.value) loading.value = false
    }
  }

  const loadNextPage = async () => {
    if (!searchSession.value?.searchId || isLoadingMore.value || !hasMoreResults.value || isPolling.value) return
    isLoadingMore.value = true
    try {
      await loadSearchSession(searchSession.value.searchId, {
        page: currentPage.value + 1,
        append: true,
      })
    } catch (cause) {
      if (!isAbortError(cause)) error.value = cause instanceof Error ? cause.message : 'Unknown error'
    } finally {
      isLoadingMore.value = false
    }
  }

  const dispose = () => {
    activeSearchGeneration += 1
    latestSessionRequestId += 1
    stopPolling()
    if (filterRefreshTimer !== null) window.clearTimeout(filterRefreshTimer)
    searchRequestController?.abort()
    searchSessionController?.abort()
  }

  return {
    loading,
    error,
    response,
    searchSession,
    loadedResults,
    currentPage,
    isLoadingMore,
    isPolling,
    hasMoreResults,
    lastExecutedSearchKey,
    search,
    scheduleRefresh,
    loadNextPage,
    dispose,
  }
}
