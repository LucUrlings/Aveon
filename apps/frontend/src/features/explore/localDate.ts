const padDatePart = (value: number) => String(value).padStart(2, '0')

export const toLocalDateInputValue = (date: Date) =>
  `${date.getFullYear()}-${padDatePart(date.getMonth() + 1)}-${padDatePart(date.getDate())}`

export const localDateWithOffset = (offset: number, now = new Date()) => {
  const date = new Date(now)
  date.setDate(date.getDate() + offset)
  return toLocalDateInputValue(date)
}
