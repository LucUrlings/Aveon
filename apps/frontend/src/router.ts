import { createRouter, createWebHistory } from 'vue-router'
import FlightSearch from './components/FlightSearch.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'search',
      component: FlightSearch,
    },
    {
      path: '/about',
      name: 'about',
      component: () => import('./pages/AboutPage.vue'),
      meta: { title: 'About Aveon' },
    },
    {
      path: '/how-it-works',
      name: 'how-it-works',
      component: () => import('./pages/HowSearchWorksPage.vue'),
      meta: { title: 'How search works' },
    },
    {
      path: '/:pathMatch(.*)*',
      redirect: '/',
    },
  ],
  scrollBehavior: () => ({ top: 0 }),
})

router.afterEach((to) => {
  if (typeof to.meta.title === 'string') {
    document.title = `${to.meta.title} · Aveon`
  }
})
