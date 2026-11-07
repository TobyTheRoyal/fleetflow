import { z } from 'zod'
import type { components } from './api.generated'

type Schemas = components['schemas']
export type Shipment = Schemas['ShipmentResponse']
export type ShipmentStatus = Schemas['ShipmentStatus']
export type Customer = Schemas['CustomerResponse']
export type Driver = Schemas['DriverResponse']
export type Vehicle = Schemas['VehicleResponse']
export type ShipmentEvent = Schemas['ShipmentEventResponse']
export type Dashboard = Schemas['DashboardResponse']
export type CreateShipment = Schemas['CreateShipmentRequest']
export type UpdateShipment = Schemas['UpdateShipmentRequest']
export type DriverInput = Schemas['DriverRequest']
export type VehicleInput = Schemas['VehicleRequest']
export type CustomerInput = Schemas['CreateCustomerRequest']
export type Page<T> = {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}
export const statuses = [
  'Draft',
  'Planned',
  'InTransit',
  'Delivered',
  'Cancelled',
] as const
const timestamp = z.iso.datetime({ offset: true })
const id = z.uuid()
const address = z.object({
  street: z.string(),
  city: z.string(),
  postcode: z.string(),
  countryCode: z.string(),
})
export const shipmentSchema: z.ZodType<Shipment> = z.object({
  id,
  shipmentNumber: z.string(),
  customerId: id,
  origin: address,
  destination: address,
  pickupWindowStartUtc: timestamp,
  deliveryDeadlineUtc: timestamp,
  weightKg: z.number(),
  palletCount: z.number().int(),
  cargoDescription: z.string().nullable(),
  status: z.enum(statuses),
  assignedDriverId: id.nullable(),
  assignedVehicleId: id.nullable(),
  actualPickupAtUtc: timestamp.nullable(),
  actualDeliveredAtUtc: timestamp.nullable(),
  createdAtUtc: timestamp,
  updatedAtUtc: timestamp,
  version: z.number().int().positive(),
  customerName: z.string().nullable(),
  assignedDriverName: z.string().nullable(),
  assignedVehiclePlateNumber: z.string().nullable(),
})
export const customerSchema: z.ZodType<Customer> = z.object({
  id,
  name: z.string(),
  contactName: z.string(),
  email: z.string(),
  phone: z.string().nullable(),
  createdAtUtc: timestamp,
})
export const driverSchema: z.ZodType<Driver> = z.object({
  id,
  employeeNumber: z.string(),
  firstName: z.string(),
  lastName: z.string(),
  active: z.boolean(),
  licenseCategory: z.string().nullable(),
  createdAtUtc: timestamp,
})
export const vehicleSchema: z.ZodType<Vehicle> = z.object({
  id,
  plateNumber: z.string(),
  type: z.enum(['Van', 'Truck']),
  maxPayloadKg: z.number(),
  status: z.enum(['Available', 'Maintenance', 'Inactive']),
  createdAtUtc: timestamp,
})
export const eventSchema: z.ZodType<ShipmentEvent> = z.object({
  id,
  shipmentId: id,
  eventType: z.enum([
    'Created',
    'AssignmentChanged',
    'StatusChanged',
    'DetailsChanged',
  ]),
  oldStatus: z.enum(statuses).nullable(),
  newStatus: z.enum(statuses).nullable(),
  message: z.string().nullable(),
  occurredAtUtc: timestamp,
  actor: z.string(),
})
export const dashboardSchema: z.ZodType<Dashboard> = z.object({
  asOfUtc: timestamp,
  totalNonCancelledShipments: z.number().int(),
  draftShipments: z.number().int(),
  plannedShipments: z.number().int(),
  inTransitShipments: z.number().int(),
  deliveredShipments: z.number().int(),
  overdueShipments: z.number().int(),
  dueSoonShipments: z.number().int(),
  recentShipments: z.array(shipmentSchema),
})
export function pageSchema<T>(item: z.ZodType<T>): z.ZodType<Page<T>> {
  return z.object({
    items: z.array(item),
    page: z.number().int(),
    pageSize: z.number().int(),
    totalCount: z.number().int(),
    totalPages: z.number().int(),
  })
}
