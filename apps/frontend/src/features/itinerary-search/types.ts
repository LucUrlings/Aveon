export type AirportGroupRequest = { id: string; label: string; airportCodes: string[] }
export type OrderedLegRequest = { id: string; from: AirportGroupRequest; to: AirportGroupRequest; departureDate: string; airportContinuityWithPrevious: 'sameAirport' | 'allowSwitch' }
export type DestinationRequest = { group: AirportGroupRequest; stay: { mode: 'minimumNights' | 'exactNights'; nights: number }; airportContinuity: 'inherit' | 'sameAirport' | 'allowSwitch' }

export type OrderedTripRequest = { mode: 'ordered'; legs: OrderedLegRequest[]; adults: number; cabinClass: string; ranking: Ranking }
export type OptimizedTripRequest = { mode: 'optimize'; start: AirportGroupRequest; destinations: DestinationRequest[]; endpointMode: string; fixedEnd: AirportGroupRequest | null; startDate: string; endDate: string; defaultAirportContinuity: string; adults: number; cabinClass: string; ranking: Ranking }
export type ItinerarySearchRequest = OptimizedTripRequest | OrderedTripRequest
export type Ranking = 'recommended' | 'cheapest' | 'fastest'

export type ItinerarySegment = { marketingCarrierName: string; marketingCarrierCode: string; flightNumber: string; originAirport: string; destinationAirport: string; departureLocalTime: string; arrivalLocalTime: string; durationMinutes: number }
export type ItineraryLeg = { id: string; originAirport: string; destinationAirport: string; departureLocalTime: string; arrivalLocalTime: string; durationMinutes: number; stops: number; segments: ItinerarySegment[] }
export type BookingOption = { label: string; url: string; price: number; currency: string; provider: string }
export type ItineraryResult = { id: string; bookingType: string; destinationOrder: string[]; legs: ItineraryLeg[]; totalPrice: number; currency: string; totalFlightDurationMinutes: number; totalStops: number; bookingCount: number; airportSwitches: number; bookingOptions: BookingOption[]; warnings: { code: string; message: string }[]; rankingBreakdown: { score: number } }
export type ItineraryFilterOption = { value: string; label: string; count: number }
export type ItineraryFilters = { airlines: ItineraryFilterOption[]; bookingSources: ItineraryFilterOption[]; departureAirports: ItineraryFilterOption[]; arrivalAirports: ItineraryFilterOption[]; minPrice?: number | null; maxPrice?: number | null; maxDurationMinutes?: number | null; maxBookingCount?: number | null; maxAirportSwitches?: number | null }
export type ItineraryPagination = { page: number; pageSize: number; totalResults: number; totalPages: number }
export type ItinerarySearchSession = { searchId: string; mode: string; status: string; phase: string; progress: number; results: ItineraryResult[]; warnings: { code: string; message: string }[]; errorMessage?: string | null; filters?: ItineraryFilters | null; pagination?: ItineraryPagination | null }

export type ItineraryResultsQuery = {
  ranking?: Ranking; direct?: boolean; oneStop?: boolean; twoPlusStops?: boolean
  airlines?: string[]; bookingSources?: string[]; departureAirports?: string[]; arrivalAirports?: string[]
  maxPrice?: number; maxDurationMinutes?: number; departureTime?: [number, number]; arrivalTime?: [number, number]
  bookingType?: string; maxBookingCount?: number; allowAirportSwitches?: boolean; page?: number; pageSize?: number
}
