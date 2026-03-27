<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

const props = defineProps<{
  maxRangeDays: number
}>()

const startDate = defineModel<string>('startDate', { required: true })
const endDate = defineModel<string>('endDate', { required: true })

const isOpen = ref(false)
const pickerRoot = ref<HTMLElement | null>(null)
const displayMonth = ref('')
const anchorDate = ref<string | null>(null)

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
  if (!startDate.value || !endDate.value) {
    return 'Select travel dates'
  }

  const start = triggerFormatter.format(parseDate(startDate.value))
  const end = triggerFormatter.format(parseDate(endDate.value))

  return startDate.value === endDate.value ? start : `${start} - ${end}`
})

const isWithinSelectedRange = (dateValue: string) =>
  dateValue >= startDate.value && dateValue <= endDate.value

const isRangeStart = (dateValue: string) => dateValue === startDate.value

const isRangeEnd = (dateValue: string) => dateValue === endDate.value

const isDisabledDate = (dateValue: string) => {
  if (dateValue < todayDateString) {
    return true
  }

  if (!anchorDate.value) {
    return false
  }

  const earliest = addDays(anchorDate.value, -(props.maxRangeDays - 1))
  const latest = addDays(anchorDate.value, props.maxRangeDays - 1)
  return dateValue < earliest || dateValue > latest
}

const selectDate = (dateValue: string) => {
  if (isDisabledDate(dateValue)) {
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
  displayMonth.value = startDate.value.slice(0, 7)
  isOpen.value = !isOpen.value

  if (!isOpen.value) {
    anchorDate.value = null
  }
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
      <span class="date-range-trigger-meta">Up to {{ maxRangeDays }} consecutive days</span>
    </button>

    <Transition name="date-range-popover">
      <div v-if="isOpen" class="date-range-popover">
        <div class="date-range-popover-header">
          <div class="date-range-popover-copy">
            <p>Select a start and end date</p>
            <span>Up to {{ maxRangeDays }} consecutive days</span>
          </div>
          <div class="date-range-popover-controls">
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
                    selected: isWithinSelectedRange(cell.date),
                    'range-start': isRangeStart(cell.date),
                    'range-end': isRangeEnd(cell.date),
                    anchor: anchorDate === cell.date,
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
