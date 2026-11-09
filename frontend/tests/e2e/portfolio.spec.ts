import { test, expect, type Page, type BrowserContext } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import type { Customer, Driver, Vehicle, Shipment } from '../../src/types/api'
test.use({
  baseURL: 'http://127.0.0.1:5175',
  storageState: { cookies: [], origins: [] },
})
async function enter(page: Page, mode: 'Viewer' | 'Dispatcher') {
  await page.goto('/demo')
  await page
    .getByRole('button', {
      name:
        mode === 'Viewer' ? 'Explore Read-Only Demo' : 'Start Interactive Demo',
      exact: true,
    })
    .click()
  await expect(page.getByLabel('Temporary demo session')).toBeVisible()
}
async function end(context: BrowserContext) {
  const csrf = (await (
    await context.request.get('/api/v1/auth/csrf')
  ).json()) as { token: string }
  await context.request.post('/api/v1/auth/logout', {
    headers: { 'X-CSRF-TOKEN': csrf.token },
  })
}
async function post<T>(
  context: BrowserContext,
  path: string,
  data: unknown,
): Promise<T> {
  const csrf = (await (
    await context.request.get('/api/v1/auth/csrf')
  ).json()) as { token: string }
  const result = await context.request.post('/api/v1/' + path, {
    data,
    headers: { 'X-CSRF-TOKEN': csrf.token },
  })
  expect(result.ok(), await result.text()).toBeTruthy()
  return result.json() as Promise<T>
}
test('public entry is accessible, responsive and has no password or admin access', async ({
  page,
}, info) => {
  await page.goto('/')
  await expect(page).toHaveURL(/\/demo$/)
  await expect(
    page.getByRole('button', { name: 'Explore Read-Only Demo' }),
  ).toBeEnabled()
  await expect(
    page.getByRole('button', { name: 'Start Interactive Demo' }),
  ).toBeEnabled()
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
        .analyze()
    ).violations,
  ).toEqual([])
  await page.setViewportSize({ width: 320, height: 850 })
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true)
  await info.attach('public-demo.png', {
    body: await page.screenshot(),
    contentType: 'image/png',
  })
  await expect(page.getByLabel('Password', { exact: true })).toHaveCount(0)
})
test('one-click viewer explores actual data and cannot mutate or generate AI', async ({
  page,
  context,
}) => {
  await enter(page, 'Viewer')
  try {
    await page.goto('/shipments')
    await expect(page.getByRole('table')).toBeVisible()
    await expect(
      page.getByRole('link', { name: 'New shipment', exact: true }),
    ).toHaveCount(0)
    const csrf = (await (
      await context.request.get('/api/v1/auth/csrf')
    ).json()) as { token: string }
    for (const path of ['customers', 'insights/ai-analysis']) {
      expect(
        (
          await context.request.post('/api/v1/' + path, {
            data: {},
            headers: { 'X-CSRF-TOKEN': csrf.token },
          })
        ).status(),
      ).toBe(403)
    }
    expect((await context.request.get('/api/v1/users')).status()).toBe(403)
    await page.goto('/insights')
    await expect(
      page.getByRole('heading', { name: 'AI Insights', exact: true }),
    ).toBeVisible()
  } finally {
    await end(context)
  }
})
test('isolated dispatcher lifecycle updates another tab while a different visitor stays isolated', async ({
  page,
  context,
  browser,
}) => {
  const other = await browser.newContext({ baseURL: 'http://127.0.0.1:5175' })
  const otherPage = await other.newPage()
  await enter(page, 'Dispatcher')
  await enter(otherPage, 'Dispatcher')
  try {
    const customer = await post<Customer>(context, 'customers', {
      name: 'Private portfolio customer',
      contactName: 'Fictional',
      email: 'fictional@example.test',
    })
    const driver = await post<Driver>(context, 'drivers', {
      employeeNumber: 'PORTFOLIO-DRIVER',
      firstName: 'Morgan',
      lastName: 'Example',
      active: true,
    })
    const vehicle = await post<Vehicle>(context, 'vehicles', {
      plateNumber: 'PORTFOLIO-VAN',
      type: 'Van',
      maxPayloadKg: 2500,
      status: 'Available',
    })
    let shipment = await post<Shipment>(context, 'shipments', {
      customerId: customer.id,
      origin: {
        street: 'Depot 1',
        city: 'Graz',
        postcode: '8010',
        countryCode: 'AT',
      },
      destination: {
        street: 'Terminal 2',
        city: 'Vienna',
        postcode: '1010',
        countryCode: 'AT',
      },
      pickupWindowStartUtc: '2030-10-10T08:00:00Z',
      deliveryDeadlineUtc: '2030-10-10T18:00:00Z',
      weightKg: 1000,
      palletCount: 2,
      cargoDescription: 'Fictional demo cargo',
    })
    expect(
      (await other.request.get('/api/v1/shipments/' + shipment.id)).status(),
    ).toBe(404)
    expect(
      await (await other.request.get('/api/v1/customers')).text(),
    ).not.toContain(customer.id)
    const tab = await context.newPage()
    await tab.goto('/shipments/' + shipment.id)
    await expect(tab.getByText('Connected', { exact: true })).toBeVisible()
    shipment = await post<Shipment>(
      context,
      `shipments/${shipment.id}/assign`,
      { version: shipment.version, driverId: driver.id, vehicleId: vehicle.id },
    )
    await expect(tab.getByText('Morgan Example', { exact: true })).toBeVisible()
    for (const status of ['Planned', 'InTransit', 'Delivered']) {
      shipment = await post<Shipment>(
        context,
        `shipments/${shipment.id}/status`,
        {
          version: shipment.version,
          status,
          reason: 'Fictional portfolio transport',
        },
      )
      await expect(tab.locator('.detail-header .status-badge')).toHaveText(
        status === 'InTransit' ? 'In transit' : status,
      )
    }
    await expect(
      tab.getByRole('heading', { name: 'In transit → Delivered' }),
    ).toBeVisible()
    await tab.close()
    await page.goto('/customers')
    await expect(
      page.getByText('Private portfolio customer', { exact: true }),
    ).toBeVisible()
    await otherPage.goto('/customers')
    await expect(
      otherPage.getByText('Private portfolio customer', { exact: true }),
    ).toHaveCount(0)
  } finally {
    await end(context)
    await end(other)
    await other.close()
  }
})
test('ending a lease revokes access and a new visitor receives a clean workspace', async ({
  page,
  context,
}) => {
  await enter(page, 'Dispatcher')
  await post(context, 'customers', {
    name: 'Discarded visitor data',
    contactName: 'Fictional',
    email: 'discard@example.test',
  })
  await page.getByRole('button', { name: 'End demo', exact: true }).click()
  await page.getByRole('button', { name: 'End demo and discard edits' }).click()
  await expect(page).toHaveURL(/\/demo$/)
  expect((await context.request.get('/api/v1/customers')).status()).toBe(401)
  await enter(page, 'Dispatcher')
  try {
    expect(
      await (await context.request.get('/api/v1/customers')).text(),
    ).not.toContain('Discarded visitor data')
  } finally {
    await end(context)
  }
})
