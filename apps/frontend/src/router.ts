import { createRouter, createWebHistory, type LocationQuery } from 'vue-router'
import FlightSearch from './components/FlightSearch.vue'
import HomePage from './pages/HomePage.vue'
import { applyPageMetadata, type PageMetadata } from './seo'

declare module 'vue-router' {
  interface RouteMeta {
    seo?: Omit<PageMetadata, 'path'>
  }
}

const legacySearchQueryKeys = new Set(['origins', 'destinations', 'dates'])

export const isLegacyRootSearch = (path: string, query: LocationQuery) =>
  path === '/' && Object.keys(query).some((key) => legacySearchQueryKeys.has(key))

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'home',
      component: HomePage,
      meta: {
        seo: {
          title: 'Aveon · Flexible and multi-destination flight discovery',
          description: 'Search flexible airports and dates, build an exact route, or optimize a bounded multi-destination journey with transparent results.',
        },
      },
    },
    {
      path: '/search',
      name: 'search',
      component: FlightSearch,
      meta: {
        seo: {
          title: 'Search flexible flights · Aveon',
          description: 'Search flights across multiple nearby airports and flexible dates, compare outbound options, and discover compatible returns.',
        },
      },
    },
    {
      path: '/explore',
      name: 'explore',
      component: () => import('./pages/ExplorePage.vue'),
      meta: {
        seo: {
          title: 'Explore direct flight destinations · Aveon',
          description: 'Choose an airport and explore its current direct-flight network on an interactive globe before continuing to flight search.',
        },
      },
    },
    {
      path: '/about',
      name: 'about',
      component: () => import('./pages/AboutPage.vue'),
      meta: {
        seo: {
          title: 'About Aveon · A wider way to discover flights',
          description: 'The best flight may be the one you were not going to search for. Learn how Aveon turns flexible airports and dates into journeys you can genuinely compare.',
        },
      },
    },
    {
      path: '/multi-destination',
      name: 'multi-destination-search',
      component: () => import('./pages/MultiDestinationSearchPage.vue'),
      meta: {
        seo: {
          title: 'Multi-destination travel search · Aveon',
          description: 'Build an exact multi-destination route or optimize destination order and stays within transparent search limits.',
        },
      },
    },
    {
      path: '/how-it-works',
      name: 'how-it-works',
      component: () => import('./pages/HowSearchWorksPage.vue'),
      meta: {
        seo: {
          title: 'How Aveon flight search works',
          description: 'See how Aveon searches multiple airports and dates progressively, groups fares, and builds compatible return options without overwhelming your browser.',
        },
      },
    },
    {
      path: '/:pathMatch(.*)*',
      redirect: '/',
    },
  ],
  scrollBehavior: () => ({ top: 0 }),
})

router.beforeEach((to) => {
  if (!isLegacyRootSearch(to.path, to.query)) return true
  return { path: '/search', query: to.query, replace: true }
})

router.afterEach((to) => {
  if (to.meta.seo) applyPageMetadata({ ...to.meta.seo, path: to.path })
})
