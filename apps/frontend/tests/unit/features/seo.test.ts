import { beforeEach, describe, expect, it } from 'vitest'
import { applyPageMetadata } from '../../../src/seo'

beforeEach(() => {
  document.head.innerHTML = ''
})

describe('applyPageMetadata', () => {
  it('sets route-specific search, canonical, social, and structured metadata', () => {
    applyPageMetadata({
      title: 'About Aveon',
      description: 'Discover why Aveon exists.',
      path: '/about',
    }, 'https://flights.example')

    expect(document.title).toBe('About Aveon')
    expect(document.querySelector<HTMLMetaElement>('meta[name="description"]')?.content).toBe('Discover why Aveon exists.')
    expect(document.querySelector<HTMLMetaElement>('meta[name="robots"]')?.content).toContain('max-snippet:-1')
    expect(document.querySelector<HTMLLinkElement>('link[rel="canonical"]')?.href).toBe('https://flights.example/about')
    expect(document.querySelector<HTMLMetaElement>('meta[property="og:url"]')?.content).toBe('https://flights.example/about')
    expect(document.querySelector<HTMLMetaElement>('meta[name="twitter:title"]')?.content).toBe('About Aveon')
    expect(document.querySelector<HTMLMetaElement>('meta[property="og:locale"]')?.content).toBe('en_IE')

    const structuredData = JSON.parse(
      document.querySelector<HTMLScriptElement>('script[data-aveon-structured-data]')?.textContent ?? '{}',
    )
    expect(structuredData).toMatchObject({
      '@type': 'WebPage',
      name: 'About Aveon',
      url: 'https://flights.example/about',
    })
  })

  it('identifies the search page as a travel web application', () => {
    applyPageMetadata({
      title: 'Aveon',
      description: 'Flexible flight search.',
      path: '/',
    }, 'https://flights.example')

    const structuredData = JSON.parse(
      document.querySelector<HTMLScriptElement>('script[data-aveon-structured-data]')?.textContent ?? '{}',
    )
    expect(structuredData).toMatchObject({
      '@type': 'WebApplication',
      applicationCategory: 'TravelApplication',
      operatingSystem: 'Any',
    })
  })

  it('uses the server-provided runtime origin by default', () => {
    window.__AVEON_CONFIG__ = { publicUrl: 'https://runtime.example' }
    applyPageMetadata({ title: 'Aveon', description: 'Search flights.', path: '/about' })

    expect(document.querySelector<HTMLLinkElement>('link[rel="canonical"]')?.href)
      .toBe('https://runtime.example/about')
    delete window.__AVEON_CONFIG__
  })
})
