import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

const publicUrlPlaceholder = '__AVEON_PUBLIC_URL__'

export const normalizePublicUrl = (value: string) => {
  const candidate = value.includes('://') ? value : `https://${value}`
  let url: URL
  try {
    url = new URL(candidate)
  } catch {
    throw new Error('AVEON_PUBLIC_URL must be a valid HTTP or HTTPS URL.')
  }

  if (!['http:', 'https:'].includes(url.protocol) || !url.hostname) {
    throw new Error('AVEON_PUBLIC_URL must be a valid HTTP or HTTPS URL.')
  }

  return url.origin
}

export const replacePublicUrlPlaceholders = (html: string, configuredUrl: string) =>
  html.replaceAll(publicUrlPlaceholder, normalizePublicUrl(configuredUrl))

const robotsContent = (origin: string) => `User-agent: *
Allow: /

Sitemap: ${origin}/sitemap.xml
`

const sitemapContent = (origin: string) => `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>${origin}/</loc>
    <changefreq>weekly</changefreq>
    <priority>1.0</priority>
  </url>
  <url>
    <loc>${origin}/search</loc>
    <changefreq>weekly</changefreq>
    <priority>1.0</priority>
  </url>
  <url>
    <loc>${origin}/about</loc>
    <changefreq>monthly</changefreq>
    <priority>0.7</priority>
  </url>
  <url>
    <loc>${origin}/how-it-works</loc>
    <changefreq>monthly</changefreq>
    <priority>0.8</priority>
  </url>
  <url>
    <loc>${origin}/multi-destination</loc>
    <changefreq>weekly</changefreq>
    <priority>0.9</priority>
  </url>
</urlset>
`

const writeWhenChanged = (path: string, content: string) => {
  let existing: string | null = null
  try {
    existing = readFileSync(path, 'utf8')
  } catch {
    // Missing files are created below.
  }
  if (existing !== content) writeFileSync(path, content, 'utf8')
}

export const generateSeoFiles = (publicDirectory: string, configuredUrl: string) => {
  const origin = normalizePublicUrl(configuredUrl)
  mkdirSync(publicDirectory, { recursive: true })
  writeWhenChanged(join(publicDirectory, 'robots.txt'), robotsContent(origin))
  writeWhenChanged(join(publicDirectory, 'sitemap.xml'), sitemapContent(origin))
}

export const removeGeneratedSeoFiles = (publicDirectory: string) => {
  rmSync(join(publicDirectory, 'robots.txt'), { force: true })
  rmSync(join(publicDirectory, 'sitemap.xml'), { force: true })
}
