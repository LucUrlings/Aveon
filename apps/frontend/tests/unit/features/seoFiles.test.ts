// @vitest-environment node
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'
import {
  generateSeoFiles,
  normalizePublicUrl,
  removeGeneratedSeoFiles,
  replacePublicUrlPlaceholders,
} from '../../../config/seoFiles'

const directories: string[] = []

afterEach(() => {
  for (const directory of directories.splice(0)) rmSync(directory, { recursive: true, force: true })
})

describe('frontend SEO file generation', () => {
  it('creates robots and sitemap files using the configured public origin', () => {
    const directory = mkdtempSync(join(tmpdir(), 'aveon-seo-'))
    directories.push(directory)

    generateSeoFiles(directory, 'https://preview.example/path')

    expect(readFileSync(join(directory, 'robots.txt'), 'utf8'))
      .toContain('Sitemap: https://preview.example/sitemap.xml')
    const sitemap = readFileSync(join(directory, 'sitemap.xml'), 'utf8')
    expect(sitemap).toContain('<loc>https://preview.example/</loc>')
    expect(sitemap).toContain('<loc>https://preview.example/search</loc>')
    expect(sitemap).toContain('<loc>https://preview.example/explore</loc>')
    expect(sitemap).toContain('<loc>https://preview.example/about</loc>')
    expect(sitemap).toContain('<loc>https://preview.example/multi-destination</loc>')
  })

  it('updates existing files when the configured URL changes', () => {
    const directory = mkdtempSync(join(tmpdir(), 'aveon-seo-'))
    directories.push(directory)
    generateSeoFiles(directory, 'first.example')

    generateSeoFiles(directory, 'second.example')

    expect(readFileSync(join(directory, 'robots.txt'), 'utf8')).not.toContain('first.example')
    expect(readFileSync(join(directory, 'sitemap.xml'), 'utf8')).toContain('https://second.example/about')
  })

  it('removes stale generated files before a runtime-configured container build', () => {
    const directory = mkdtempSync(join(tmpdir(), 'aveon-seo-'))
    directories.push(directory)
    generateSeoFiles(directory, 'stale.example')

    removeGeneratedSeoFiles(directory)

    expect(() => readFileSync(join(directory, 'robots.txt'), 'utf8')).toThrow()
    expect(() => readFileSync(join(directory, 'sitemap.xml'), 'utf8')).toThrow()
  })

  it('normalizes URLs and replaces index metadata placeholders', () => {
    expect(normalizePublicUrl('aveon.example/path')).toBe('https://aveon.example')
    expect(replacePublicUrlPlaceholders(
      '<link href="__AVEON_PUBLIC_URL__/"><meta content="__AVEON_PUBLIC_URL__/about">',
      'http://localhost:4173/',
    )).toBe('<link href="http://localhost:4173/"><meta content="http://localhost:4173/about">')
    expect(() => normalizePublicUrl('ftp://aveon.example')).toThrow(/HTTP or HTTPS/)
  })
})
