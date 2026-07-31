import { ref, watch } from 'vue'

export type TripType = 'oneWay' | 'return'

const toDateInputValue = (date: Date) => date.toISOString().slice(0, 10)

const addDays = (dateString: string, days: number) => {
  const date = new Date(`${dateString}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

const buildDateRange = (start: string | null, end: string | null) => {
  if (!start || !end) {
    return []
  }

  const first = start <= end ? start : end
  const last = start <= end ? end : start
  const dates: string[] = []
  for (let date = first; date <= last; date = addDays(date, 1)) {
    dates.push(date)
  }
  return dates
}

export const useSearchDates = (onSwitchToOneWay: () => void) => {
  const today = new Date()
  const initialDepartureDates = [7, 8, 9].map((daysAhead) => toDateInputValue(new Date(Date.UTC(
    today.getFullYear(),
    today.getMonth(),
    today.getDate() + daysAhead,
  ))))

  const tripType = ref<TripType>('oneWay')
  const departureDateFrom = ref(initialDepartureDates[0])
  const departureDateTo = ref(initialDepartureDates.at(-1)!)
  const selectedDepartureDates = ref([...initialDepartureDates])
  const returnDateFrom = ref<string | null>(null)
  const returnDateTo = ref<string | null>(null)
  const selectedReturnDates = ref<string[]>([])

  watch(departureDateFrom, (value) => {
    if (departureDateTo.value < value) {
      departureDateTo.value = value
      return
    }
    const maxAllowedEnd = addDays(value, 9)
    if (departureDateTo.value > maxAllowedEnd) {
      departureDateTo.value = maxAllowedEnd
    }
  })

  watch(departureDateTo, (value) => {
    if (departureDateFrom.value > value) {
      departureDateFrom.value = value
      return
    }
    const minAllowedStart = addDays(value, -9)
    if (departureDateFrom.value < minAllowedStart) {
      departureDateFrom.value = minAllowedStart
    }
    if (tripType.value === 'return') {
      const validDates = selectedReturnDates.value.filter((date) => date >= value)
      selectedReturnDates.value = validDates.length > 0 ? validDates : [value]
      returnDateFrom.value = selectedReturnDates.value[0]
      returnDateTo.value = selectedReturnDates.value.at(-1) ?? value
    }
  })

  watch(tripType, (value) => {
    if (value === 'oneWay') {
      returnDateFrom.value = null
      returnDateTo.value = null
      selectedReturnDates.value = []
      onSwitchToOneWay()
      return
    }

    returnDateFrom.value ??= departureDateTo.value
    returnDateTo.value ??= returnDateFrom.value
    if (selectedReturnDates.value.length === 0) {
      selectedReturnDates.value = buildDateRange(returnDateFrom.value, returnDateTo.value)
    }
  })

  watch(returnDateFrom, (value) => {
    if (!value) return
    if (value < departureDateTo.value) {
      returnDateFrom.value = departureDateTo.value
      return
    }
    if (returnDateTo.value && returnDateTo.value < value) {
      returnDateTo.value = value
    }
  })

  watch(returnDateTo, (value) => {
    if (value && returnDateFrom.value && value < returnDateFrom.value) {
      returnDateTo.value = returnDateFrom.value
    }
  })

  return {
    tripType,
    departureDateFrom,
    departureDateTo,
    selectedDepartureDates,
    returnDateFrom,
    returnDateTo,
    selectedReturnDates,
  }
}
