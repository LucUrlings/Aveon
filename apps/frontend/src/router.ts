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
  ],
})
