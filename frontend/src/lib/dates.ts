import { Temporal } from '@js-temporal/polyfill'
export const TIME_ZONE = 'Europe/Vienna'
const dateFormatter = new Intl.DateTimeFormat('en-GB', {
  timeZone: TIME_ZONE,
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})
export function formatDate(value: string | null) {
  return value ? dateFormatter.format(new Date(value)) : 'Not recorded'
}
export function localInput(value: string) {
  return Temporal.Instant.from(value)
    .toZonedDateTimeISO(TIME_ZONE)
    .toPlainDateTime()
    .toString({ smallestUnit: 'minute' })
}
export function toUtc(value: string, original?: string) {
  if (original && localInput(original) === value) return original
  return Temporal.PlainDateTime.from(value)
    .toZonedDateTime(TIME_ZONE, { disambiguation: 'reject' })
    .toInstant()
    .toString()
}
export function isOverdue(
  shipment: { status: string; deliveryDeadlineUtc: string },
  now = Date.now(),
) {
  return (
    !['Delivered', 'Cancelled'].includes(shipment.status) &&
    new Date(shipment.deliveryDeadlineUtc).getTime() < now
  )
}
export const number = new Intl.NumberFormat('en-GB')
export const kilograms = (weight: number) => `${number.format(weight)} kg`
