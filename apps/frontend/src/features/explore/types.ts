export type ExploreAirport = {
  code: string
  name: string
  city: string
  country: string
  latitude: number
  longitude: number
}

export type ExploreRoutesResponse = {
  origin: ExploreAirport
  destinations: ExploreAirport[]
  observedFrom: string
  observedTo: string
  fetchedAt: string
  isComplete: boolean
  isStale: boolean
}
