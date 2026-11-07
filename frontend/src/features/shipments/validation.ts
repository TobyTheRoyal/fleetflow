import { z } from 'zod'
import { toUtc, localInput } from '@/lib/dates'
import type { Shipment, CreateShipment } from '@/types/api'
const text = (label: string, max: number) =>
  z
    .string()
    .trim()
    .min(1, `${label} is required.`)
    .max(max, `Use at most ${max} characters.`)
export const addressSchema = z.object({
  street: text('Street', 200),
  city: text('City', 100),
  postcode: text('Postcode', 20),
  countryCode: z
    .string()
    .trim()
    .toUpperCase()
    .regex(/^[A-Z]{2}$/, 'Use a two-letter country code, for example AT.'),
})
export function shipmentFormSchema(initial?: Shipment) {
  return z
    .object({
      customerId: z.uuid({ error: 'Select a customer.' }),
      origin: addressSchema,
      destination: addressSchema,
      pickup: z.string().min(1, 'Pickup time is required.'),
      deadline: z.string().min(1, 'Delivery deadline is required.'),
      weightKg: z
        .number({ error: 'Enter a valid cargo weight.' })
        .positive('Cargo weight must be greater than zero.')
        .max(9999999999.99)
        .refine(
          (value) => Math.abs(value * 100 - Math.round(value * 100)) < 0.0001,
          'Use at most two decimal places.',
        ),
      palletCount: z
        .number({ error: 'Enter a valid pallet count.' })
        .int('Use a whole number.')
        .min(0, 'Pallet count cannot be negative.'),
      cargoDescription: z.string().max(1000, 'Use at most 1000 characters.'),
    })
    .superRefine((data, context) => {
      let pickup: string | undefined
      let deadline: string | undefined
      try {
        pickup = toUtc(data.pickup, initial?.pickupWindowStartUtc)
      } catch {
        context.addIssue({
          code: 'custom',
          path: ['pickup'],
          message:
            'Choose an unambiguous local time in Europe/Vienna. This time may not exist or may occur twice during a clock change.',
        })
      }
      try {
        deadline = toUtc(data.deadline, initial?.deliveryDeadlineUtc)
      } catch {
        context.addIssue({
          code: 'custom',
          path: ['deadline'],
          message: 'Choose an unambiguous local time in Europe/Vienna.',
        })
      }
      if (pickup && deadline && Date.parse(deadline) <= Date.parse(pickup))
        context.addIssue({
          code: 'custom',
          path: ['deadline'],
          message: 'Delivery deadline must be after pickup.',
        })
    })
}
export type ShipmentFormValues = z.infer<ReturnType<typeof shipmentFormSchema>>
export function defaults(initial?: Shipment): ShipmentFormValues {
  return initial
    ? {
        customerId: initial.customerId,
        origin: initial.origin,
        destination: initial.destination,
        pickup: localInput(initial.pickupWindowStartUtc),
        deadline: localInput(initial.deliveryDeadlineUtc),
        weightKg: initial.weightKg,
        palletCount: initial.palletCount,
        cargoDescription: initial.cargoDescription ?? '',
      }
    : {
        customerId: '',
        origin: { street: '', city: '', postcode: '', countryCode: 'AT' },
        destination: { street: '', city: '', postcode: '', countryCode: 'AT' },
        pickup: '',
        deadline: '',
        weightKg: 0,
        palletCount: 0,
        cargoDescription: '',
      }
}
export function payload(
  values: ShipmentFormValues,
  initial?: Shipment,
): CreateShipment {
  if (initial?.status === 'Planned')
    return {
      customerId: initial.customerId,
      origin: initial.origin,
      destination: initial.destination,
      pickupWindowStartUtc: initial.pickupWindowStartUtc,
      deliveryDeadlineUtc: initial.deliveryDeadlineUtc,
      weightKg: initial.weightKg,
      palletCount: initial.palletCount,
      cargoDescription: values.cargoDescription.trim() || null,
    }
  return {
    customerId: values.customerId,
    origin: values.origin,
    destination: values.destination,
    pickupWindowStartUtc: toUtc(values.pickup, initial?.pickupWindowStartUtc),
    deliveryDeadlineUtc: toUtc(values.deadline, initial?.deliveryDeadlineUtc),
    weightKg: values.weightKg,
    palletCount: values.palletCount,
    cargoDescription: values.cargoDescription.trim() || null,
  }
}
