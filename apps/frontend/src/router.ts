import { createRouter, createWebHistory } from 'vue-router'
import FlightSearch from './components/FlightSearch.vue'
import { applyPageMetadata, type PageMetadata } from './seo'

declare module 'vue-router' {
  interface RouteMeta {
    seo?: Omit<PageMetadata, 'path'>
  }
}

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'search',
      component: FlightSearch,
      meta: {
        seo: {
          title: 'Aveon · Flexible flight search across airports and dates',
          description: 'Search flights across multiple nearby airports and flexible dates at once. Compare outbound flights, then discover compatible return options.',
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
          description: 'Build ordered routes or describe a multi-destination trip using reusable airport groups.',
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

router.afterEach((to) => {
  if (to.meta.seo) applyPageMetadata({ ...to.meta.seo, path: to.path })
})
