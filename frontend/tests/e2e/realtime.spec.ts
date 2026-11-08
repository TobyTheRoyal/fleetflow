import {
  test,
  expect,
  type APIRequestContext,
  type Page,
  type WebSocketRoute,
} from '@playwright/test'
import { randomBytes } from 'node:crypto'
import { authenticatedState } from './infra/auth'
import type { Shipment, Customer, Driver, Vehicle } from '../../src/types/api'
async function csrf(request: APIRequestContext) {
  return (
    (await (await request.get('/api/v1/auth/csrf')).json()) as { token: string }
  ).token
}
async function post<T>(
  request: APIRequestContext,
  path: string,
  data: unknown,
): Promise<T> {
  const response = await request.post('/api/v1/' + path, {
    data,
    headers: { 'X-CSRF-TOKEN': await csrf(request) },
  })
  expect(response.ok()).toBeTruthy()
  return response.json()
}
async function login(page: Page, account: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Email', { exact: true }).fill(account.email)
  await page.getByLabel('Password', { exact: true }).fill(account.password)
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(
    page.getByRole('status', { name: 'Real-time: Connected', exact: true }),
  ).toBeVisible()
}
async function changeStatus(page: Page, label: string) {
  await page.getByRole('button', { name: label, exact: true }).click()
  await page
    .getByRole('dialog')
    .getByRole('button', { name: label, exact: true })
    .click()
  await expect(page.getByRole('dialog')).toBeHidden()
}
test('two dispatchers collaborate live, retain edits, recover missed events and log out cleanly', async ({
  browser,
  playwright,
  baseURL,
}, info) => {
  test.setTimeout(90_000)
  const admin = await playwright.request.newContext({
    baseURL,
    storageState: await authenticatedState(playwright, baseURL!, 'Admin'),
  })
  const accounts = []
  for (const name of ['A', 'B']) {
    const account = {
      email: `${randomBytes(10).toString('hex')}@example.test`,
      displayName: `Live Dispatcher ${name}`,
      role: 'Dispatcher',
      password: 'Ff!7' + randomBytes(24).toString('hex'),
    }
    await post(admin, 'users', account)
    accounts.push(account)
  }
  const contextA = await browser.newContext()
  const contextB = await browser.newContext()
  let offline = false
  const sockets = new Set<{ client: WebSocketRoute; server: WebSocketRoute }>()
  await contextB.routeWebSocket('**/hubs/logistics**', (socket) => {
    if (offline) {
      socket.close()
      return
    }
    sockets.add({ client: socket, server: socket.connectToServer() })
  })
  const pageA = await contextA.newPage()
  const pageB = await contextB.newPage()
  try {
    await login(pageA, accounts[0])
    await login(pageB, accounts[1])
    const request = pageA.request
    const label = `RT-${info.project.name.slice(0, 2)}-${info.repeatEachIndex}`
    const customer = await post<Customer>(request, 'customers', {
      name: 'Real-time Demo',
      contactName: 'Demo Contact',
      email: 'live@example.test',
    })
    const driver = await post<Driver>(request, 'drivers', {
      employeeNumber: label,
      firstName: 'Live',
      lastName: 'Driver',
      active: true,
    })
    const vehicle = await post<Vehicle>(request, 'vehicles', {
      plateNumber: label,
      type: 'Truck',
      maxPayloadKg: 12000,
      status: 'Available',
    })
    let shipment = await post<Shipment>(request, 'shipments', {
      customerId: customer.id,
      origin: {
        street: 'Depot 1',
        city: 'Graz',
        postcode: '8010',
        countryCode: 'AT',
      },
      destination: {
        street: 'Depot 2',
        city: 'Vienna',
        postcode: '1010',
        countryCode: 'AT',
      },
      pickupWindowStartUtc: '2031-06-12T08:00:00Z',
      deliveryDeadlineUtc: '2031-06-12T18:00:00Z',
      weightKg: 1000,
      palletCount: 2,
      cargoDescription: 'Live cargo',
    })
    shipment = await post(request, `shipments/${shipment.id}/assign`, {
      version: shipment.version,
      driverId: driver.id,
      vehicleId: vehicle.id,
    })
    await pageA.goto(`/shipments/${shipment.id}`)
    await pageB.goto(`/shipments/${shipment.id}`)
    await expect(
      pageB.getByRole('status', { name: 'Real-time: Connected', exact: true }),
    ).toBeVisible()
    await expect(
      pageA.getByRole('status', { name: 'Real-time: Connected', exact: true }),
    ).toBeVisible()
    // Same page stays open; no navigation, reload, focus-triggered refresh or polling interval.
    await changeStatus(pageA, 'Plan shipment')
    await expect(pageB.locator('.detail-header .status-badge')).toHaveText(
      'Planned',
      { timeout: 10_000 },
    )
    await expect(
      pageB.getByRole('heading', { name: 'Draft → Planned' }),
    ).toBeVisible()
    await pageB.getByRole('link', { name: 'Edit shipment' }).click()
    await pageB
      .getByLabel('Cargo description (optional)')
      .fill('Dispatcher B unsaved notes')
    await changeStatus(pageA, 'Start transit')
    await expect(pageB.getByLabel('Cargo description (optional)')).toHaveValue(
      'Dispatcher B unsaved notes',
    )
    await pageB.getByRole('button', { name: 'Save changes' }).click()
    await expect(pageB.getByRole('alert')).toContainText(
      'Shipment changed elsewhere',
    )
    await expect(pageB.getByLabel('Cargo description (optional)')).toHaveValue(
      'Dispatcher B unsaved notes',
    )
    await pageB
      .getByRole('button', { name: 'Discard edits and reload latest' })
      .click()
    await pageB
      .getByRole('link', { name: 'Return to shipment', exact: true })
      .click()
    await expect(pageB.locator('.detail-header .status-badge')).toHaveText(
      'In transit',
    )
    offline = true
    await contextB.setOffline(true)
    for (const socket of sockets) {
      socket.server.close()
      socket.client.close({
        code: 1012,
        reason: 'Test connection interruption',
      })
    }
    await expect(
      pageB.getByRole('status', {
        name: /Real-time: (Reconnecting|Disconnected)/,
      }),
    ).toBeVisible()
    await changeStatus(pageA, 'Mark delivered')
    await expect(pageB.locator('.detail-header .status-badge')).toHaveText(
      'In transit',
    )
    offline = false
    await contextB.setOffline(false)
    await expect(
      pageB.getByRole('status', { name: 'Real-time: Connected', exact: true }),
    ).toBeVisible({ timeout: 20_000 })
    await expect(pageB.locator('.detail-header .status-badge')).toHaveText(
      'Delivered',
    )
    await expect(
      pageB.getByRole('heading', { name: 'In transit → Delivered' }),
    ).toBeVisible()
    await pageB.getByRole('button', { name: 'Sign out', exact: true }).click()
    await expect(pageB).toHaveURL(/\/login$/)
    expect((await pageB.request.get('/api/v1/auth/me')).status()).toBe(401)
  } finally {
    await contextA.close()
    await contextB.close()
    await admin.dispose()
  }
})

test('deactivating a connected viewer ends live access and preserves the session recovery UI', async ({
  browser,
  playwright,
  baseURL,
}) => {
  const admin = await playwright.request.newContext({
    baseURL,
    storageState: await authenticatedState(playwright, baseURL!, 'Admin'),
  })
  const account = {
    email: `${randomBytes(10).toString('hex')}@example.test`,
    displayName: 'Live Viewer',
    role: 'Viewer',
    password: 'Ff!7' + randomBytes(24).toString('hex'),
  }
  const user = await post<{ id: string }>(admin, 'users', account)
  const context = await browser.newContext()
  const page = await context.newPage()
  try {
    await login(page, account)
    const response = await admin.put(`/api/v1/users/${user.id}/activation`, {
      data: { active: false },
      headers: { 'X-CSRF-TOKEN': await csrf(admin) },
    })
    expect(response.ok()).toBeTruthy()
    await expect(
      page.getByRole('dialog', { name: 'Session expired' }),
    ).toBeVisible({ timeout: 15_000 })
    // The modal intentionally hides background landmarks from assistive technology.
    await expect(
      page.locator('[aria-label="Real-time: Disconnected"]'),
    ).toHaveCount(1)
    expect((await page.request.get('/api/v1/shipments')).status()).toBe(401)
  } finally {
    await context.close()
    await admin.dispose()
  }
})

test('a shared-cookie account change locks the old live workspace and reauthenticates safely', async ({
  browser,
  playwright,
  baseURL,
}) => {
  const admin = await playwright.request.newContext({
    baseURL,
    storageState: await authenticatedState(playwright, baseURL!, 'Admin'),
  })
  const accounts = []
  for (const role of ['Viewer', 'Dispatcher']) {
    const account = {
      email: `${randomBytes(10).toString('hex')}@example.test`,
      displayName: `Replacement ${role}`,
      role,
      password: 'Ff!7' + randomBytes(24).toString('hex'),
    }
    await post(admin, 'users', account)
    accounts.push(account)
  }
  const context = await browser.newContext()
  const sockets: { client: WebSocketRoute; server: WebSocketRoute }[] = []
  await context.routeWebSocket('**/hubs/logistics**', (client) => {
    sockets.push({ client, server: client.connectToServer() })
  })
  const page = await context.newPage()
  try {
    await login(page, accounts[0])
    // The browser cookie changes as it would when another tab signs into this profile.
    await post(page.request, 'auth/login', {
      email: accounts[1].email,
      password: accounts[1].password,
    })
    for (const socket of [...sockets]) {
      socket.server.close()
      socket.client.close()
    }
    const dialog = page.getByRole('dialog', { name: 'Session expired' })
    await expect(dialog).toBeVisible()
    await expect(
      page.locator('[aria-label="Real-time: Disconnected"]'),
    ).toHaveCount(1)
    await dialog.getByLabel('Email', { exact: true }).fill(accounts[1].email)
    await dialog
      .getByLabel('Password', { exact: true })
      .fill(accounts[1].password)
    await dialog.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(dialog).toBeHidden()
    await expect(
      page.getByText(accounts[1].displayName, { exact: true }),
    ).toBeVisible()
    await expect(
      page.getByRole('status', { name: 'Real-time: Connected', exact: true }),
    ).toBeVisible()
  } finally {
    await context.close()
    await admin.dispose()
  }
})
