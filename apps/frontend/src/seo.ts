export type PageMetadata = {
  title: string
  description: string
  path: string
}

declare global {
  interface Window {
    __AVEON_CONFIG__?: { publicUrl?: string }
  }
}

const setMeta = (attribute: 'name' | 'property', key: string, content: string) => {
  let element = document.head.querySelector<HTMLMetaElement>(`meta[${attribute}="${key}"]`)
  if (!element) {
    element = document.createElement('meta')
    element.setAttribute(attribute, key)
    document.head.append(element)
  }
  element.content = content
}

const setCanonicalUrl = (url: string) => {
  let canonical = document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]')
  if (!canonical) {
    canonical = document.createElement('link')
    canonical.rel = 'canonical'
    document.head.append(canonical)
  }
  canonical.href = url
}

const setStructuredData = (metadata: PageMetadata, url: string) => {
  let script = document.head.querySelector<HTMLScriptElement>('script[data-aveon-structured-data]')
  if (!script) {
    script = document.createElement('script')
    script.type = 'application/ld+json'
    script.dataset.aveonStructuredData = ''
    document.head.append(script)
  }

  const isSearchApplication = metadata.path === '/search'
  script.textContent = JSON.stringify({
    '@context': 'https://schema.org',
    '@type': isSearchApplication ? 'WebApplication' : metadata.path === '/' ? 'WebSite' : 'WebPage',
    name: metadata.title,
    description: metadata.description,
    url,
    ...(isSearchApplication ? {
      applicationCategory: 'TravelApplication',
      operatingSystem: 'Any',
      browserRequirements: 'Requires JavaScript',
    } : {}),
  })
}

const getRuntimeOrigin = () => {
  const configuredUrl = window.__AVEON_CONFIG__?.publicUrl?.trim()
  return configuredUrl && !configuredUrl.startsWith('__') ? configuredUrl : window.location.origin
}

export const applyPageMetadata = (metadata: PageMetadata, origin = getRuntimeOrigin()) => {
  const canonicalUrl = new URL(metadata.path, origin).toString()
  document.title = metadata.title
  setMeta('name', 'description', metadata.description)
  setMeta('name', 'robots', 'index, follow, max-image-preview:large, max-snippet:-1, max-video-preview:-1')
  setMeta('property', 'og:type', 'website')
  setMeta('property', 'og:site_name', 'Aveon')
  setMeta('property', 'og:title', metadata.title)
  setMeta('property', 'og:description', metadata.description)
  setMeta('property', 'og:url', canonicalUrl)
  setMeta('property', 'og:locale', 'en_IE')
  setMeta('name', 'twitter:card', 'summary')
  setMeta('name', 'twitter:title', metadata.title)
  setMeta('name', 'twitter:description', metadata.description)
  setCanonicalUrl(canonicalUrl)
  setStructuredData(metadata, canonicalUrl)
}
