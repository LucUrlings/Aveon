import { ref } from 'vue'

export const useSearchFilters = () => ({
  includeDirectFlights: ref(true),
  includeOneStopFlights: ref(false),
  includeTwoPlusStopFlights: ref(false),
  selectedProviders: ref<string[]>([]),
  selectedAirlines: ref<string[]>([]),
  selectedDepartureAirports: ref<string[]>([]),
  selectedArrivalAirports: ref<string[]>([]),
  maxDurationMinutes: ref(0),
  departureTimeRange: ref<[number, number]>([0, 1439]),
  arrivalTimeRange: ref<[number, number]>([0, 1439]),
  returnDepartureTimeRange: ref<[number, number]>([0, 1439]),
  returnArrivalTimeRange: ref<[number, number]>([0, 1439]),
})
