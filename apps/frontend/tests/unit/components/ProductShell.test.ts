import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import AboutPage from '../../../src/pages/AboutPage.vue'
import AppNavbar from '../../../src/components/AppNavbar.vue'
import HomePage from '../../../src/pages/HomePage.vue'
import HowSearchWorksPage from '../../../src/pages/HowSearchWorksPage.vue'
import App from '../../../src/App.vue'

const authMocks = vi.hoisted(() => ({
  refresh: vi.fn().mockResolvedValue(undefined),
  signIn: vi.fn().mockResolvedValue(undefined),
  signUp: vi.fn().mockResolvedValue(undefined),
  signOut: vi.fn().mockResolvedValue(undefined),
}))

vi.mock('../../../src/features/auth/useAuth', () => ({
  useAuth: () => ({
    user: { value: { isAuthenticated: false, email: null } },
    isAuthenticated: { value: false },
    refresh: authMocks.refresh,
    signIn: authMocks.signIn,
    signUp: authMocks.signUp,
    signOut: authMocks.signOut,
  }),
}))

const routerLinkStub = {
  props: ['to'],
  template: '<a :href="to"><slot /></a>',
}

describe('product shell', () => {
  it('provides a keyboard skip link to the routed main content', () => {
    const wrapper = mount(App, {
      global: {
        stubs: {
          AppNavbar: true,
          RouterView: { template: '<main id="main-content" tabindex="-1">Page</main>' },
          RouterLink: routerLinkStub,
        },
      },
    })

    expect(wrapper.get('.skip-link').attributes('href')).toBe('#main-content')
    expect(wrapper.get('#main-content').attributes('tabindex')).toBe('-1')
  })

  it('offers useful product navigation and keeps authentication available', async () => {
    const wrapper = mount(AppNavbar, {
      global: { stubs: { RouterLink: routerLinkStub } },
    })
    await flushPromises()

    expect(wrapper.get('.navbar-brand').attributes('href')).toBe('/')
    expect(wrapper.get('.navbar-links a[href="/"]').text()).toBe('Home')
    expect(wrapper.get('.navbar-links a[href="/search"]').text()).toBe('Search')
    expect(wrapper.get('.navbar-links a[href="/explore"]').text()).toBe('Explore')
    expect(wrapper.get('.navbar-links').text()).toContain('Search')
    expect(wrapper.get('.navbar-links').text()).toContain('How it works')
    expect(wrapper.get('.navbar-links').text()).toContain('About')
    expect(wrapper.get('a[href="https://github.com/LucUrlings/Aveon"]').attributes('target')).toBe('_blank')
    expect(wrapper.get('.account-menu').text()).toContain('Sign in')
    expect(authMocks.refresh).toHaveBeenCalled()
  })

  it('explains the product and links to the repository and creator website', () => {
    const wrapper = mount(AboutPage, {
      global: { stubs: { RouterLink: routerLinkStub } },
    })

    expect(wrapper.get('h1').text()).toContain('one you weren’t going to search for')
    expect(wrapper.text()).toContain('searches beyond the obvious route')
    expect(wrapper.text()).toContain('Multi-destination routes')
    expect(wrapper.get('a[href="https://github.com/LucUrlings/Aveon"]').attributes('rel')).toBe('noreferrer')
    expect(wrapper.get('a[href="https://lucurlings.nl"]').attributes('target')).toBe('_blank')
    expect(wrapper.get('.about-primary-action').attributes('href')).toBe('/search')
  })

  it('presents the index as a product landing page with both search modes', () => {
    const wrapper = mount(HomePage, {
      global: { stubs: { RouterLink: routerLinkStub, HeroRouteGlobe: true } },
    })

    expect(wrapper.get('h1').text()).toContain('Compare flights across flexible airports, dates, and destinations')
    expect(wrapper.get('.hero-actions a[href="/search"]').text()).toContain('Search flights')
    expect(wrapper.get('.hero-actions a[href="/explore"]').text()).toContain('Explore destinations')
    expect(wrapper.get('.hero-actions a[href="/multi-destination"]').text()).toContain('Plan multiple destinations')
    expect(wrapper.get('.mode-grid').text()).toContain('One-way and return')
    expect(wrapper.get('.mode-grid').text()).toContain('Explore direct destinations')
    expect(wrapper.get('.mode-grid').text()).toContain('Ordered or optimized')
    expect(wrapper.get('.home-explainer').text()).toContain('metasearch product, not a booking engine')
    expect(wrapper.findComponent({ name: 'HeroRouteGlobe' }).exists()).toBe(true)
  })

  it('explains progressive searches and staged return combinations', () => {
    const wrapper = mount(HowSearchWorksPage, {
      global: { stubs: { RouterLink: routerLinkStub } },
    })

    expect(wrapper.get('h1').text()).toContain('bookable journey')
    expect(wrapper.get('.flow-list').text()).toContain('bounded concurrency')
    expect(wrapper.get('.return-section').text()).toContain('choose an outbound first')
    expect(wrapper.get('.fare-type-grid').text()).toContain('Real combination')
    expect(wrapper.get('.fare-type-grid').text()).toContain('Synthetic combination')
    expect(wrapper.text()).toContain('cannot guarantee the global cheapest route')
    expect(wrapper.get('.guide-action').attributes('href')).toBe('/search')
  })
})
