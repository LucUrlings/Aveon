<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

const props = defineProps<{
  maxRangeDays: number
}>()

const startDate = defineModel<string>('startDate', { required: true })
const endDate = defineModel<string>('endDate', { required: true })
const selectedDates = defineModel<string[]>('selectedDates', { required: true })

type SelectionMode = 'range' | 'specific'

const isOpen = ref(false)
const pickerRoot = ref<HTMLElement | null>(null)
const displayMonth = ref('')
const anchorDate = ref<string | null>(null)
const selectionMode = ref<SelectionMode>('range')

const monthFormatter = new Intl.DateTimeFormat('en-IE', {
  month: 'long',
  year: 'numeric',
})

const triggerFormatter = new Intl.DateTimeFormat('en-IE', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
})

const weekdayLabels = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su']
const monthOptions = [
  { value: '01', label: 'January' },
  { value: '02', label: 'February' },
  { value: '03', label: 'March' },
  { value: '04', label: 'April' },
  { value: '05', label: 'May' },
  { value: '06', label: 'June' },
  { value: '07', label: 'July' },
  { value: '08', label: 'August' },
  { value: '09', label: 'September' },
  { value: '10', label: 'October' },
  { value: '11', label: 'November' },
  { value: '12', label: 'December' },
]

const parseDate = (value: string) => new Date(`${value}T00:00:00Z`)

const toDateString = (date: Date) => {
  const year = date.getUTCFullYear()
  const month = String(date.getUTCMonth() + 1).padStart(2, '0')
  const day = String(date.getUTCDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

const today = new Date()
const todayDateString = toDateString(new Date(Date.UTC(
  today.getUTCFullYear(),
  today.getUTCMonth(),
  today.getUTCDate(),
)))

const addDays = (value: string, days: number) => {
  const date = parseDate(value)
  date.setUTCDate(date.getUTCDate() + days)
  return toDateString(date)
}

const addMonths = (monthValue: string, months: number) => {
  const [year, month] = monthValue.split('-').map(Number)
  const date = new Date(Date.UTC(year, month - 1, 1))
  date.setUTCMonth(date.getUTCMonth() + months)
  return toDateString(date).slice(0, 7)
}

const getMonthLabel = (monthValue: string) => {
  const [year, month] = monthValue.split('-').map(Number)
  return monthFormatter.format(new Date(Date.UTC(year, month - 1, 1)))
}

const getMonthDays = (monthValue: string) => {
  const [year, month] = monthValue.split('-').map(Number)
  const firstDay = new Date(Date.UTC(year, month - 1, 1))
  const startWeekday = (firstDay.getUTCDay() + 6) % 7
  const daysInMonth = new Date(Date.UTC(year, month, 0)).getUTCDate()

  const cells: Array<{ date: string; inMonth: boolean }> = []

  for (let index = 0; index < startWeekday; index += 1) {
    cells.push({ date: `blank-${monthValue}-${index}`, inMonth: false })
  }

  for (let day = 1; day <= daysInMonth; day += 1) {
    const date = new Date(Date.UTC(year, month - 1, day))
    cells.push({ date: toDateString(date), inMonth: true })
  }

  return cells
}

const months = computed(() => [
  displayMonth.value,
  addMonths(displayMonth.value, 1),
])

const selectedYear = computed({
  get: () => displayMonth.value.slice(0, 4),
  set: (value: string) => {
    displayMonth.value = `${value}-${selectedMonth.value}`
  },
})

const selectedMonth = computed({
  get: () => displayMonth.value.slice(5, 7),
  set: (value: string) => {
    displayMonth.value = `${selectedYear.value}-${value}`
  },
})

const yearOptions = computed(() => {
  const currentYear = Number(todayDateString.slice(0, 4))
  return Array.from({ length: 7 }, (_, index) => String(currentYear + index))
})

const availableMonthOptions = computed(() => {
  const currentYear = todayDateString.slice(0, 4)
  const currentMonth = todayDateString.slice(5, 7)

  if (selectedYear.value !== currentYear) {
    return monthOptions
  }

  return monthOptions.filter((month) => month.value >= currentMonth)
})

const formattedRange = computed(() => {
  if (selectedDates.value.length === 0) {
    return 'Select travel dates'
  }

  return selectedDates.value
    .map((value) => triggerFormatter.format(parseDate(value)))
    .join(', ')
})

const normalizeDates = (dates: string[]) => [...new Set(dates)].sort((left, right) => left.localeCompare(right))

const buildSelectedDates = (start: string, end: string) => {
  if (!start || !end) {
    return []
  }

  const first = start <= end ? start : end
  const last = start <= end ? end : start
  const dates: string[] = []

  for (let dateValue = first; dateValue <= last; dateValue = addDays(dateValue, 1)) {
    dates.push(dateValue)
  }

  return normalizeDates(dates)
}

const isWithinSelectedRange = (dateValue: string) =>
  selectionMode.value === 'range' &&
  dateValue >= startDate.value &&
  dateValue <= endDate.value

const isRangeStart = (dateValue: string) =>
  selectionMode.value === 'range' && dateValue === startDate.value

const isRangeEnd = (dateValue: string) =>
  selectionMode.value === 'range' && dateValue === endDate.value

const isSelectedDate = (dateValue: string) => selectedDates.value.includes(dateValue)

const isDisabledDate = (dateValue: string) => {
  if (dateValue < todayDateString) {
    return true
  }

  if (selectionMode.value === 'specific') {
    return selectedDates.value.length >= props.maxRangeDays && !isSelectedDate(dateValue)
  }

  if (!anchorDate.value) {
    return false
  }

  const earliest = addDays(anchorDate.value, -(props.maxRangeDays - 1))
  const latest = addDays(anchorDate.value, props.maxRangeDays - 1)
  return dateValue < earliest || dateValue > latest
}

const syncBoundsFromSelectedDates = (dates: string[]) => {
  if (dates.length === 0) {
    return
  }

  startDate.value = dates[0]
  endDate.value = dates[dates.length - 1]
}

const toggleSpecificDate = (dateValue: string) => {
  const nextDates = isSelectedDate(dateValue)
    ? selectedDates.value.filter((value) => value !== dateValue)
    : normalizeDates([...selectedDates.value, dateValue])

  selectedDates.value = nextDates
  syncBoundsFromSelectedDates(nextDates)
}

const selectDate = (dateValue: string) => {
  if (isDisabledDate(dateValue)) {
    return
  }

  if (selectionMode.value === 'specific') {
    toggleSpecificDate(dateValue)
    return
  }

  if (!anchorDate.value) {
    startDate.value = dateValue
    endDate.value = dateValue
    anchorDate.value = dateValue
    return
  }

  if (dateValue < anchorDate.value) {
    startDate.value = dateValue
    endDate.value = anchorDate.value
  } else {
    startDate.value = anchorDate.value
    endDate.value = dateValue
  }

  anchorDate.value = null
  isOpen.value = false
}

const closePicker = () => {
  isOpen.value = false
  anchorDate.value = null
}

const togglePicker = () => {
  const focalDate = selectedDates.value[0] ?? startDate.value
  displayMonth.value = focalDate.slice(0, 7)
  isOpen.value = !isOpen.value

  if (!isOpen.value) {
    anchorDate.value = null
  }
}

const setSelectionMode = (mode: SelectionMode) => {
  selectionMode.value = mode
  anchorDate.value = null
}

const moveMonth = (offset: number) => {
  displayMonth.value = addMonths(displayMonth.value, offset)
}

const handleClickOutside = (event: MouseEvent) => {
  if (!pickerRoot.value || !(event.target instanceof Node)) {
    return
  }

  if (!pickerRoot.value.contains(event.target)) {
    closePicker()
  }
}

watch(
  startDate,
  (value) => {
    if (!displayMonth.value) {
      displayMonth.value = (value < todayDateString ? todayDateString : value).slice(0, 7)
    }
  },
  { immediate: true },
)

watch(
  [startDate, endDate],
  ([start, end]) => {
    if (selectionMode.value === 'range') {
      selectedDates.value = buildSelectedDates(start, end)
    }
  },
  { immediate: true },
)

watch(
  selectedDates,
  (dates) => {
    if (dates.length > 0) {
      syncBoundsFromSelectedDates(normalizeDates(dates))
    }
  },
  { deep: true },
)

onMounted(() => {
  document.addEventListener('mousedown', handleClickOutside)
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', handleClickOutside)
})
</script>

<template>
  <div ref="pickerRoot" class="date-range-picker">
    <button type="button" class="date-range-trigger" @click="togglePicker">
      <span class="date-range-trigger-label">{{ formattedRange }}</span>
      <span class="date-range-trigger-meta">
        {{ selectionMode === 'range' ? `Up to ${maxRangeDays} consecutive days` : `Up to ${maxRangeDays} picked dates` }}
      </span>
    </button>

    <Transition name="date-range-popover">
      <div v-if="isOpen" class="date-range-popover">
        <div class="date-range-popover-header">
          <div class="date-range-popover-copy">
            <p>Select travel dates</p>
            <span>
              {{ selectionMode === 'range' ? `Range mode: up to ${maxRangeDays} consecutive days` : `Specific dates mode: up to ${maxRangeDays} picked dates` }}
            </span>
          </div>
          <div class="date-range-popover-controls">
            <div class="selection-mode-toggle">
              <button
                type="button"
                class="selection-mode-button"
                :class="{ active: selectionMode === 'range' }"
                @click="setSelectionMode('range')"
              >
                Range
              </button>
              <button
                type="button"
                class="selection-mode-button"
                :class="{ active: selectionMode === 'specific' }"
                @click="setSelectionMode('specific')"
              >
                Specific dates
              </button>
            </div>
            <div class="calendar-jumpers">
              <select v-model="selectedMonth" class="calendar-jump-select">
                <option v-for="month in availableMonthOptions" :key="month.value" :value="month.value">
                  {{ month.label }}
                </option>
              </select>
              <select v-model="selectedYear" class="calendar-jump-select">
                <option v-for="year in yearOptions" :key="year" :value="year">
                  {{ year }}
                </option>
              </select>
            </div>
            <div class="calendar-nav-group">
              <button type="button" class="calendar-nav" @click="moveMonth(-1)">Prev</button>
              <button type="button" class="calendar-nav" @click="moveMonth(1)">Next</button>
            </div>
          </div>
        </div>

        <div class="calendar-grid">
          <section v-for="month in months" :key="month" class="calendar-month">
            <header class="calendar-month-header">{{ getMonthLabel(month) }}</header>
            <div class="calendar-weekdays">
              <span v-for="weekday in weekdayLabels" :key="weekday">{{ weekday }}</span>
            </div>
            <div class="calendar-days">
              <template v-for="cell in getMonthDays(month)" :key="cell.date">
                <span v-if="!cell.inMonth" class="calendar-day spacer" />
                <button
                  v-else
                  type="button"
                  class="calendar-day"
                  :class="{
                    selected: isWithinSelectedRange(cell.date) || (selectionMode === 'specific' && isSelectedDate(cell.date)),
                    'range-start': isRangeStart(cell.date),
                    'range-end': isRangeEnd(cell.date),
                    anchor: selectionMode === 'range' && anchorDate === cell.date,
                    disabled: isDisabledDate(cell.date),
                  }"
                  :disabled="isDisabledDate(cell.date)"
                  @click="selectDate(cell.date)"
                >
                  <span class="calendar-day-number">{{ Number(cell.date.slice(-2)) }}</span>
                </button>
              </template>
            </div>
          </section>
        </div>
      </div>
    </Transition>
  </div>
</template>

<style scoped src="./DateRangePicker.css"></style>
