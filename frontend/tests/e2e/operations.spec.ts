import { test, expect } from './infra/test'
import { type APIRequestContext, type Page } from '@playwright/test'
import type {
  Customer,
  Driver,
  Vehicle,
  Shipment,
  Dashboard,
  Page as ApiPage,
  ShipmentEvent,
} from '../../src/types/api'
let customer: Customer, driver: Driver, vehicle: Vehicle
const pickup = '2030-06-12T08:00:00Z',
  deadline = '2030-06-12T18:00:00Z'
async function post<T>(
  request: APIRequestContext,
  path: string,
  data: unknown,
): Promise<T> {
  const r = await request.post('/api/v1/' + path, { data })
  expect(r.ok(), await r.text()).toBeTruthy()
  return r.json()
}
const input = () => ({
  customerId: customer.id,
  origin: {
    street: 'Terminal 12',
    city: 'Vienna',
    postcode: '1010',
    countryCode: 'AT',
  },
  destination: {
    street: 'Depot 4',
    city: 'Linz',
    postcode: '4020',
    countryCode: 'AT',
  },
  pickupWindowStartUtc: pickup,
  deliveryDeadlineUtc: deadline,
  weightKg: 1200.5,
  palletCount: 3,
  cargoDescription: 'E2E precision machinery',
})
test.beforeEach(async ({ request }, info) => {
  customer = await post(request, 'customers', {
    name: 'E2E Shipping Co',
    contactName: 'Demo Dispatcher',
    email: 'dispatch@example.test',
    phone: null,
  })
  driver = await post(request, 'drivers', {
    employeeNumber:
      'E2E-' +
      info.project.name.slice(0, 2) +
      '-' +
      info.testId.slice(-8) +
      info.repeatEachIndex,
    firstName: 'Alex',
    lastName: 'Testdriver',
    active: true,
    licenseCategory: 'C',
  })
  vehicle = await post(request, 'vehicles', {
    plateNumber:
      'E2E-' +
      info.project.name.slice(0, 2) +
      '-' +
      info.testId.slice(-8) +
      info.repeatEachIndex,
    type: 'Truck',
    maxPayloadKg: 18000,
    status: 'Available',
  })
})
async function assign(page: Page) {
  await page
    .getByRole('button', { name: 'Assign driver & vehicle', exact: true })
    .click()
  await page.getByLabel('Driver', { exact: true }).selectOption(driver.id)
  await page.getByLabel('Vehicle', { exact: true }).selectOption(vehicle.id)
  await page.getByRole('button', { name: 'Save assignment' }).click()
}
async function status(page: Page, label: string) {
  await page.getByRole('button', { name: label, exact: true }).click()
  await page
    .getByRole('dialog')
    .getByRole('button', { name: label, exact: true })
    .click()
  await expect(page.getByRole('dialog')).toBeHidden()
}
test('dashboard and server-side listing reflect PostgreSQL data', async ({
  page,
  request,
}) => {
  const summary: Dashboard = await (
    await request.get('/api/v1/dashboard/summary')
  ).json()
  await page.goto('/')
  await expect(
    page
      .locator('.kpi-card')
      .filter({ hasText: 'Total shipments' })
      .locator('.kpi-number'),
  ).toHaveText(String(summary.totalNonCancelledShipments))
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('link', { name: 'Shipments' })
    .click()
  await expect(page.getByRole('table')).toBeVisible()
  await page.getByLabel('Filter by status').selectOption('Draft')
  await page.getByRole('button', { name: 'Apply filters' }).click()
  await expect(page).toHaveURL(/status=Draft/)
  await expect(page.locator('tbody .status-badge').first()).toHaveText('Draft')
  await page.getByLabel('Rows per page').selectOption('10')
  await expect(page).toHaveURL(/pageSize=10/)
})
test('create, edit, dispatch, plan, transport and deliver with immutable timeline', async ({
  page,
  request,
}) => {
  await page.goto('/shipments/new')
  await page.getByLabel('Customer', { exact: true }).selectOption(customer.id)
  for (const [side, address] of [
    ['Origin', input().origin],
    ['Destination', input().destination],
  ] as const) {
    await page.getByLabel(`${side} street and number`).fill(address.street)
    await page.getByLabel(`${side} city`, { exact: true }).fill(address.city)
    await page.getByLabel(`${side} postcode`).fill(address.postcode)
  }
  await page.getByLabel('Weight (kg)').fill('1200.5')
  await page.getByLabel('Pallet count').fill('3')
  await page
    .getByLabel('Cargo description (optional)')
    .fill('E2E precision machinery')
  await page.getByLabel('Pickup time', { exact: true }).fill('2030-06-12T10:00')
  await page
    .getByLabel('Delivery deadline', { exact: true })
    .fill('2030-06-12T20:00')
  await page
    .getByRole('button', { name: 'Create shipment', exact: true })
    .click()
  await expect(page).toHaveURL(/\/shipments\/[0-9a-f-]{36}$/)
  const id = page.url().split('/').pop()!
  await page.getByRole('link', { name: 'Edit shipment' }).click()
  await page
    .getByLabel('Cargo description (optional)')
    .fill('E2E sealed machinery')
  await page.getByRole('button', { name: 'Save changes' }).click()
  await expect(
    page.getByRole('heading', { name: 'Route & schedule' }),
  ).toBeVisible()
  await assign(page)
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(page.getByText('Alex Testdriver', { exact: true })).toBeVisible()
  await status(page, 'Plan shipment')
  await page.getByRole('link', { name: 'Edit shipment' }).click()
  await expect(page.getByLabel('Weight (kg)')).toHaveCount(0)
  await page
    .getByLabel('Cargo description (optional)')
    .fill('E2E planned notes')
  await page.getByRole('button', { name: 'Save changes' }).click()
  await status(page, 'Start transit')
  await expect(page.getByRole('link', { name: 'Edit shipment' })).toHaveCount(0)
  await status(page, 'Mark delivered')
  await expect(
    page.getByRole('heading', { name: 'In transit → Delivered' }),
  ).toBeVisible()
  const saved: Shipment = await (
    await request.get(`/api/v1/shipments/${id}`)
  ).json()
  expect(saved.status).toBe('Delivered')
  expect(Date.parse(saved.pickupWindowStartUtc)).toBe(Date.parse(pickup))
  const events: ApiPage<ShipmentEvent> = await (
    await request.get(`/api/v1/shipments/${id}/events`)
  ).json()
  expect(events.totalCount).toBe(7)
  expect(
    events.items.filter((e) => e.eventType === 'StatusChanged'),
  ).toHaveLength(3)
  await page.reload()
  await expect(
    page.getByRole('heading', { name: 'In transit → Delivered' }),
  ).toBeVisible()
})
test('a concurrent update requires deliberate reload and never overwrites', async ({
  page,
  request,
}) => {
  let shipment = await post<Shipment>(request, 'shipments', input())
  shipment = await post(request, `shipments/${shipment.id}/assign`, {
    version: shipment.version,
    driverId: driver.id,
    vehicleId: vehicle.id,
  })
  await page.goto(`/shipments/${shipment.id}`)
  await page.getByRole('button', { name: 'Plan shipment', exact: true }).click()
  await post(request, `shipments/${shipment.id}/status`, {
    version: shipment.version,
    status: 'Cancelled',
    reason: 'Another operator cancelled',
  })
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Plan shipment', exact: true })
    .click()
  await expect(page.getByRole('alert')).toContainText(
    'Shipment changed elsewhere',
  )
  await expect(
    page
      .getByRole('dialog')
      .getByRole('button', { name: 'Plan shipment', exact: true }),
  ).toBeDisabled()
  await page
    .getByRole('button', { name: 'Discard edits and reload latest' })
    .click()
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(page.locator('.detail-header .status-badge')).toHaveText(
    'Cancelled',
  )
  const events: ApiPage<ShipmentEvent> = await (
    await request.get(`/api/v1/shipments/${shipment.id}/events`)
  ).json()
  expect(events.totalCount).toBe(3)
  expect(events.items.at(-1)?.newStatus).toBe('Cancelled')
})
test('real overlapping reservations show actionable conflict and keep history unchanged', async ({
  page,
  request,
}) => {
  let reserved = await post<Shipment>(request, 'shipments', input())
  reserved = await post(request, `shipments/${reserved.id}/assign`, {
    version: reserved.version,
    driverId: driver.id,
    vehicleId: vehicle.id,
  })
  reserved = await post(request, `shipments/${reserved.id}/status`, {
    version: reserved.version,
    status: 'Planned',
    reason: null,
  })
  const target = await post<Shipment>(request, 'shipments', input())
  try {
    await page.goto(`/shipments/${target.id}`)
    await assign(page)
    await expect(page.getByRole('alert')).toContainText(
      /overlap|reserved|conflict/i,
    )
    const events: ApiPage<ShipmentEvent> = await (
      await request.get(`/api/v1/shipments/${target.id}/events`)
    ).json()
    expect(events.totalCount).toBe(1)
  } finally {
    await post(request, `shipments/${reserved.id}/status`, {
      version: reserved.version,
      status: 'Cancelled',
      reason: 'Release test reservation',
    })
  }
})
test('master-data creation/editing and mobile keyboard navigation work', async ({
  page,
}, info) => {
  const label = `${info.project.name}-${info.repeatEachIndex}`

  await page.goto('/customers')
  await page.getByRole('button', { name: 'Add customer', exact: true }).click()
  await page.getByLabel('Company name').fill(`Aardvark E2E ${label} Demo`)
  await page.getByLabel('Contact name').fill('Taylor Demo')
  await page.getByLabel('Email', { exact: true }).fill('taylor@example.test')
  await page.getByRole('button', { name: 'Create record' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(
    page.getByText(`Aardvark E2E ${label} Demo`, { exact: true }),
  ).toBeVisible()
  await page.goto('/drivers')
  await page.getByRole('button', { name: 'Add driver', exact: true }).click()
  await page.getByLabel('Employee number').fill(`AAA-${label}`)
  await page.getByLabel('First name').fill('Jordan')
  await page.getByLabel('Last name').fill(`Example ${label}`)
  await page.getByRole('button', { name: 'Create record' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()
  await page
    .getByRole('row')
    .filter({ hasText: `Jordan Example ${label}` })
    .getByRole('button', { name: 'Edit' })
    .click()
  await page.getByLabel('Active driver').uncheck()
  await page.getByRole('button', { name: 'Save changes' }).click()
  await expect(
    page.getByRole('row').filter({ hasText: `Jordan Example ${label}` }),
  ).toContainText('Inactive')
  await page.goto('/vehicles')
  await page.getByRole('button', { name: 'Add vehicle', exact: true }).click()
  await page.getByLabel('Registration plate').fill(`AAA-${label}`)
  await page.getByLabel('Vehicle type').selectOption('Van')
  await page.getByRole('button', { name: 'Create record' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()
  await page
    .getByRole('row')
    .filter({ hasText: `AAA-${label}` })
    .getByRole('button', { name: 'Edit' })
    .click()
  await page
    .getByLabel('Availability', { exact: true })
    .selectOption('Maintenance')
  await page.getByRole('button', { name: 'Save changes' }).click()
  await expect(
    page.getByRole('row').filter({ hasText: `AAA-${label}` }),
  ).toContainText('Maintenance')
  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('button', { name: 'Open navigation' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(
    page.getByRole('button', { name: 'Open navigation' }),
  ).toBeFocused()
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBeTruthy()
})
