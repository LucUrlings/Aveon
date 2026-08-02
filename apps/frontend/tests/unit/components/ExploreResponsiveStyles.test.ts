import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

const explorePageSource = readFileSync(join(process.cwd(), 'src/pages/ExplorePage.vue'), 'utf8')
const heroGlobeSource = readFileSync(join(process.cwd(), 'src/features/explore/HeroRouteGlobe.vue'), 'utf8')
const homePageStyles = readFileSync(join(process.cwd(), 'src/pages/HomePage.css'), 'utf8')

describe('Explore responsive layout contracts', () => {
  it('collapses the homepage and Explore result grids at their documented breakpoints', () => {
    expect(homePageStyles).toContain('@media (max-width: 900px)')
    expect(homePageStyles).toContain('.home-hero { grid-template-columns: 1fr;')
    expect(explorePageSource).toContain('@media (max-width: 820px)')
    expect(explorePageSource).toContain('.origin-panel, .explore-grid { grid-template-columns: 1fr;')
    expect(explorePageSource).toContain('.explore-grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(280px, 340px); align-items: start;')
    expect(explorePageSource).toContain('.globe-column :deep(.globe-shell) { height: 600px; min-height: 600px;')
    expect(explorePageSource).toContain('.globe-column :deep(.globe-shell) { height: 480px; min-height: 480px;')
    expect(explorePageSource).toContain('.globe-column :deep(.globe-shell) { height: 360px; min-height: 360px;')
    expect(explorePageSource).toContain('.route-selection-enter-active')
    expect(explorePageSource).toContain('.destination-move')
    expect(explorePageSource).toContain('.explore-results--updating')
    expect(heroGlobeSource).toContain('@media (max-width: 680px)')
  })
})
