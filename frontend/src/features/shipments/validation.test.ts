import { expect, it } from 'vitest'
import { defaults, payload, shipmentFormSchema } from './validation'
import { shipment } from '@/test/fixtures'
import { toUtc } from '@/lib/dates'
it('preserves exact original instants when minute precision inputs are unchanged', () => {
  expect(payload(defaults(shipment), shipment).pickupWindowStartUtc).toBe(
    shipment.pickupWindowStartUtc,
  )
})
it('changes only cargo description for Planned records', () => {
  const initial = { ...shipment, status: 'Planned' as const }
  const values = {
    ...defaults(initial),
    weightKg: 9000,
    cargoDescription: 'New notes',
  }
  expect(payload(values, initial)).toEqual({
    customerId: shipment.customerId,
    origin: shipment.origin,
    destination: shipment.destination,
    pickupWindowStartUtc: shipment.pickupWindowStartUtc,
    deliveryDeadlineUtc: shipment.deliveryDeadlineUtc,
    weightKg: shipment.weightKg,
    palletCount: shipment.palletCount,
    cargoDescription: 'New notes',
  })
})
it.each(['2030-03-31T02:30', '2030-10-27T02:30'])(
  'rejects nonexistent or ambiguous DST time %s',
  (value) => {
    expect(() => toUtc(value)).toThrow()
  },
)
it('converts Vienna summer schedule to UTC', () => {
  expect(toUtc('2030-06-12T10:00')).toBe('2030-06-12T08:00:00Z')
})
it('rejects invalid scheduling, weights and countries', () => {
  const result = shipmentFormSchema().safeParse({
    ...defaults(shipment),
    weightKg: -1,
    origin: { ...shipment.origin, countryCode: 'Austria' },
    deadline: '2030-06-11T10:00',
  })
  expect(result.success).toBe(false)
})
