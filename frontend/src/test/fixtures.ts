import type {
  Customer,
  Driver,
  Vehicle,
  Shipment,
  Dashboard,
  ShipmentEvent,
} from '@/types/api'
export const customer: Customer = {
  id: '10000000-0000-4000-8000-000000000001',
  name: 'Danube Demo Logistics',
  contactName: 'Taylor Demo',
  email: 'demo@example.test',
  phone: null,
  createdAtUtc: '2026-11-01T08:00:00Z',
}
export const driver: Driver = {
  id: '20000000-0000-4000-8000-000000000001',
  employeeNumber: 'EMP-001',
  firstName: 'Alex',
  lastName: 'Demo',
  active: true,
  licenseCategory: 'C',
  createdAtUtc: customer.createdAtUtc,
}
export const vehicle: Vehicle = {
  id: '30000000-0000-4000-8000-000000000001',
  plateNumber: 'W-DEMO',
  type: 'Truck',
  maxPayloadKg: 12000,
  status: 'Available',
  createdAtUtc: customer.createdAtUtc,
}
export const shipment: Shipment = {
  id: '40000000-0000-4000-8000-000000000001',
  shipmentNumber: 'FF-2026-000001',
  customerId: customer.id,
  origin: {
    street: 'Terminal 1',
    city: 'Vienna',
    postcode: '1010',
    countryCode: 'AT',
  },
  destination: {
    street: 'Depot 2',
    city: 'Linz',
    postcode: '4020',
    countryCode: 'AT',
  },
  pickupWindowStartUtc: '2030-06-12T08:00:12Z',
  deliveryDeadlineUtc: '2030-06-12T18:00:19Z',
  weightKg: 1200.5,
  palletCount: 3,
  cargoDescription: 'Demo machinery',
  status: 'Draft',
  assignedDriverId: null,
  assignedVehicleId: null,
  actualPickupAtUtc: null,
  actualDeliveredAtUtc: null,
  createdAtUtc: customer.createdAtUtc,
  updatedAtUtc: customer.createdAtUtc,
  version: 1,
  customerName: customer.name,
  assignedDriverName: null,
  assignedVehiclePlateNumber: null,
}
export const assigned: Shipment = {
  ...shipment,
  assignedDriverId: driver.id,
  assignedVehicleId: vehicle.id,
  assignedDriverName: 'Alex Demo',
  assignedVehiclePlateNumber: vehicle.plateNumber,
  version: 2,
}
export const event: ShipmentEvent = {
  id: '50000000-0000-4000-8000-000000000001',
  shipmentId: shipment.id,
  eventType: 'Created',
  oldStatus: null,
  newStatus: 'Draft',
  message: null,
  occurredAtUtc: customer.createdAtUtc,
  actor: 'local-dispatcher',
}
export const dashboard: Dashboard = {
  asOfUtc: '2026-11-06T12:00:00Z',
  totalNonCancelledShipments: 25,
  draftShipments: 7,
  plannedShipments: 6,
  inTransitShipments: 4,
  deliveredShipments: 8,
  overdueShipments: 2,
  dueSoonShipments: 3,
  recentShipments: [shipment],
}
export function page<T>(
  items: T[],
  overrides: Partial<{
    page: number
    pageSize: number
    totalCount: number
    totalPages: number
  }> = {},
) {
  return {
    items,
    page: 1,
    pageSize: 20,
    totalCount: items.length,
    totalPages: items.length ? 1 : 0,
    ...overrides,
  }
}
