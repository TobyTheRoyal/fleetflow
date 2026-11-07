import {
  test,
  expect,
  type APIRequestContext,
  type Page,
  type PlaywrightWorkerArgs,
} from '@playwright/test'
import { authenticatedState } from './infra/auth'
import { randomBytes } from 'node:crypto'
import AxeBuilder from '@axe-core/playwright'
const password = () => 'Ff!7' + randomBytes(24).toString('hex')
async function csrf(client: APIRequestContext) {
  return (
    (await (await client.get('/api/v1/auth/csrf')).json()) as { token: string }
  ).token
}
async function createUser(
  playwright: PlaywrightWorkerArgs['playwright'],
  role: string,
) {
  const client = await playwright.request.newContext({
    baseURL: 'http://127.0.0.1:5174',
    storageState: await authenticatedState(
      playwright,
      'http://127.0.0.1:5174',
      'Admin',
    ),
  })
  const account = {
    email: `${randomBytes(10).toString('hex')}@example.test`,
    displayName: `Browser ${role}`,
    role,
    password: password(),
  }
  const response = await client.post('/api/v1/users', {
    data: account,
    headers: { 'X-CSRF-TOKEN': await csrf(client) },
  })
  expect(response.status()).toBe(201)
  await client.dispose()
  return account
}
async function login(page: Page, account: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Email', { exact: true }).fill(account.email)
  await page.getByLabel('Password', { exact: true }).fill(account.password)
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(
    page.getByRole('heading', { name: 'Operations overview' }),
  ).toBeVisible()
}
test('login, failed login, session restoration, CSRF and logout', async ({
  page,
  playwright,
  context,
}) => {
  const account = await createUser(playwright, 'Dispatcher')
  expect((await page.request.get('/api/v1/shipments')).status()).toBe(401)
  await page.goto('/login')
  const axe = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()
  expect(axe.violations).toEqual([])
  await page.getByLabel('Email', { exact: true }).fill(account.email)
  await page
    .getByLabel('Password', { exact: true })
    .fill('Incorrect-password12!')
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText(
    'Email or password is incorrect',
  )
  await page.getByLabel('Password', { exact: true }).fill(account.password)
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(
    page.getByRole('heading', { name: 'Operations overview' }),
  ).toBeVisible()
  const cookie = (await context.cookies()).find(
    (c) => c.name === 'FleetFlow.Session',
  )
  expect(cookie?.httpOnly).toBe(true)
  expect(cookie?.sameSite).toBe('Strict')
  expect(await page.evaluate(() => Object.keys(localStorage))).toEqual([])
  expect((await page.request.post('/api/v1/auth/logout')).status()).toBe(400)
  await page
    .getByRole('button', { name: 'Change password', exact: true })
    .click()
  await page
    .getByLabel('Current password', { exact: true })
    .fill(account.password)
  const next = password()
  await page.getByLabel('New password', { exact: true }).fill(next)
  await page
    .getByRole('button', { name: 'Update password', exact: true })
    .click()
  await expect(page.getByRole('dialog')).toBeHidden()
  account.password = next

  await page.reload()
  await expect(
    page.getByRole('heading', { name: 'Operations overview' }),
  ).toBeVisible()
  await page.getByRole('button', { name: 'Sign out', exact: true }).click()
  await expect(
    page.getByRole('heading', { name: 'Welcome back' }),
  ).toBeVisible()
  expect((await page.request.get('/api/v1/auth/me')).status()).toBe(401)
})
test('Viewer can read but UI and API both forbid writes and administration', async ({
  page,
  playwright,
}) => {
  const account = await createUser(playwright, 'Viewer')
  await login(page, account)
  await expect(page.getByRole('link', { name: 'New shipment' })).toHaveCount(0)
  await page.goto('/customers')
  await expect(page.getByRole('table')).toBeVisible()
  await expect(
    page.getByRole('button', { name: 'Add customer', exact: true }),
  ).toHaveCount(0)
  expect(
    (
      await page.request.post('/api/v1/customers', {
        data: {},
        headers: { 'X-CSRF-TOKEN': await csrf(page.request) },
      })
    ).status(),
  ).toBe(403)
  expect((await page.request.get('/api/v1/users')).status()).toBe(403)
  await page.goto('/shipments/new')
  await expect(page.getByRole('alert')).toContainText(
    'Your role does not permit',
  )
})
test('Admin provisions, deactivates and reactivates a user through the UI', async ({
  page,
  playwright,
}) => {
  await login(page, await createUser(playwright, 'Admin'))
  await page.getByRole('link', { name: 'Users', exact: true }).click()
  await page.getByRole('button', { name: 'Add user' }).click()
  const email = `${randomBytes(10).toString('hex')}@example.test`,
    secret = password()
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Display name').fill('Managed browser viewer')
  await page.getByLabel('Role', { exact: true }).selectOption('Viewer')
  await page.getByLabel('Initial password').fill(secret)
  await page.getByRole('button', { name: 'Create user', exact: true }).click()
  await expect(page.getByRole('dialog')).toBeHidden()
  const viewer = await playwright.request.newContext({
    baseURL: 'http://127.0.0.1:5174',
  })
  try {
    expect(
      (
        await viewer.post('/api/v1/auth/login', {
          data: { email, password: secret },
          headers: { 'X-CSRF-TOKEN': await csrf(viewer) },
        })
      ).status(),
    ).toBe(200)
    await page
      .getByRole('button', { name: `Deactivate ${email}`, exact: true })
      .click()
    await page.getByRole('button', { name: 'Confirm access change' }).click()
    await expect(page.getByRole('dialog')).toBeHidden()
    expect((await viewer.get('/api/v1/auth/me')).status()).toBe(401)
    expect(
      (
        await viewer.post('/api/v1/auth/login', {
          data: { email, password: secret },
          headers: { 'X-CSRF-TOKEN': await csrf(viewer) },
        })
      ).status(),
    ).toBe(401)
    await page
      .getByRole('button', { name: `Activate ${email}`, exact: true })
      .click()
    await page.getByRole('button', { name: 'Confirm access change' }).click()
    await expect(page.getByRole('dialog')).toBeHidden()
    expect(
      (
        await viewer.post('/api/v1/auth/login', {
          data: { email, password: secret },
          headers: { 'X-CSRF-TOKEN': await csrf(viewer) },
        })
      ).status(),
    ).toBe(200)
  } finally {
    await viewer.dispose()
  }
})
test('lost session preserves a draft until the same account signs in again', async ({
  page,
  playwright,
  context,
}) => {
  const account = await createUser(playwright, 'Dispatcher')
  await login(page, account)
  const customers = (await (
    await page.request.get('/api/v1/customers?pageSize=1')
  ).json()) as { items: { id: string }[] }
  const response = await page.request.post('/api/v1/shipments', {
    headers: { 'X-CSRF-TOKEN': await csrf(page.request) },
    data: {
      customerId: customers.items[0]!.id,
      origin: {
        street: 'Demo Terminal 1',
        city: 'Graz',
        postcode: '8010',
        countryCode: 'AT',
      },
      destination: {
        street: 'Demo Depot 2',
        city: 'Vienna',
        postcode: '1010',
        countryCode: 'AT',
      },
      pickupWindowStartUtc: '2032-06-12T08:00:00Z',
      deliveryDeadlineUtc: '2032-06-12T18:00:00Z',
      weightKg: 100,
      palletCount: 1,
      cargoDescription: 'Original cargo',
    },
  })
  expect(response.status()).toBe(201)
  const shipment = (await response.json()) as { id: string }
  await page.goto(`/shipments/${shipment.id}/edit`)
  await page
    .getByLabel('Cargo description (optional)')
    .fill('Unsaved fragile cargo')
  // Simulate losing an expired session cookie; actual timed expiry is a PostgreSQL HTTP test.
  await context.clearCookies({ name: 'FleetFlow.Session' })
  await page.getByRole('button', { name: 'Save changes', exact: true }).click()
  await expect(
    page.getByRole('dialog', { name: 'Session expired' }),
  ).toBeVisible()
  await page.getByLabel('Password', { exact: true }).fill(account.password)
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(page.getByLabel('Cargo description (optional)')).toHaveValue(
    'Unsaved fragile cargo',
  )
  await page.getByRole('button', { name: 'Save changes', exact: true }).click()
  await expect(
    page.getByText('Unsaved fragile cargo', { exact: true }),
  ).toBeVisible()
})
