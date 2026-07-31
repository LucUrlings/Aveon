import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import AboutPage from '../../../src/pages/AboutPage.vue'
import AppNavbar from '../../../src/components/AppNavbar.vue'
import HowSearchWorksPage from '../../../src/pages/HowSearchWorksPage.vue'

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
  it('offers useful product navigation and keeps authentication available', async () => {
    const wrapper = mount(AppNavbar, {
      global: { stubs: { RouterLink: routerLinkStub } },
    })
    await flushPromises()

    expect(wrapper.get('.navbar-brand').attributes('href')).toBe('/')
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
    expect(wrapper.get('a[href="https://github.com/LucUrlings/Aveon"]').attributes('rel')).toBe('noreferrer')
    expect(wrapper.get('a[href="https://lucurlings.nl"]').attributes('target')).toBe('_blank')
    expect(wrapper.get('.about-primary-action').attributes('href')).toBe('/')
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
  })
})
