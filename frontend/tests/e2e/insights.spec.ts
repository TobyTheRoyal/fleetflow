import { test, expect } from './infra/test'
import { authenticatedState } from './infra/auth'
import { randomBytes } from 'node:crypto'
import type { Customer, Shipment } from '../../src/types/api'
import type { components } from '../../src/types/api.generated'

test('verified insights, evidence, scoped shipment links and controlled on-demand AI work with the fake provider', async ({
  browser,
  playwright,
  baseURL,
  request,
}, info) => {
  const customerResponse = await request.post('/api/v1/customers', {
    data: {
      name: `Insight E2E ${info.project.name}`,
      contactName: 'Fictional Dispatcher',
      email: 'insights@example.test',
      phone: null,
    },
  })
  expect(customerResponse.status()).toBe(201)
  const customer: Customer = await customerResponse.json()
  const created = await request.post('/api/v1/shipments', {
    data: {
      customerId: customer.id,
      origin: {
        street: 'Demo depot 1',
        city: 'Graz',
        postcode: '8010',
        countryCode: 'AT',
      },
      destination: {
        street: 'Demo depot 2',
        city: 'Vienna',
        postcode: '1010',
        countryCode: 'AT',
      },
      pickupWindowStartUtc: '2020-01-01T08:00:00Z',
      deliveryDeadlineUtc: '2020-01-02T08:00:00Z',
      weightKg: 1100,
      palletCount: 2,
      cargoDescription: 'Fictional precision equipment',
    },
  })
  expect(created.status()).toBe(201)
  const shipment: Shipment = await created.json()
  const baseline: components['schemas']['InsightSummary'] = await (
    await request.get('/api/v1/insights/summary')
  ).json()
  const role =
    info.project.name === 'chromium'
      ? 'Admin'
      : info.project.name === 'firefox'
        ? 'Dispatcher'
        : 'Viewer'
  const own = await browser.newContext({
    storageState: await authenticatedState(playwright, baseURL!, role),
  })
  const page = await own.newPage()
  try {
    const before: components['schemas']['AiAvailability'] = await (
      await page.request.get(baseURL + '/api/v1/insights/ai-availability')
    ).json()
    let posts = 0
    page.on('request', (r) => {
      if (r.method() === 'POST' && r.url().includes('/insights/ai-analysis'))
        posts++
    })
    await page.goto(`${baseURL}/insights?shipmentId=${shipment.id}`)
    await expect(
      page.getByRole('heading', { name: 'AI Insights', exact: true }),
    ).toBeVisible()
    await expect(
      page.getByRole('heading', { name: 'Delivery overdue', exact: true }),
    ).toBeVisible()
    await page.getByText('Supporting evidence').first().click()
    await expect(page.getByText('Draft · Version 1').first()).toBeVisible()
    expect(posts).toBe(0)
    const button = page.getByRole('button', {
      name: 'Generate AI Analysis',
      exact: true,
    })
    await expect(button).toBeEnabled()
    await button.click()
    await expect(
      page.getByText('AI-selected suggestion:').first(),
    ).toBeVisible()
    expect(posts).toBe(1)
    const after: components['schemas']['AiAvailability'] = await (
      await page.request.get(baseURL + '/api/v1/insights/ai-availability')
    ).json()
    expect(after.budget.userRemaining).toBe(before.budget.userRemaining - 1)
    await expect(page.getByLabel('Daily AI allowance')).toContainText(
      `${after.budget.userRemaining} of 3`,
    )
    const unchanged: Shipment = await (
      await request.get(`/api/v1/shipments/${shipment.id}`)
    ).json()
    expect(unchanged.version).toBe(shipment.version)
    expect(unchanged.status).toBe('Draft')
    expect(after.budget.userRemaining).toBeGreaterThan(0)
    {
      await expect(button).toBeEnabled()
      await button.click()
      await expect(
        page.getByText(/Cached result; no new provider request/),
      ).toBeVisible()
      const cached: components['schemas']['AiAvailability'] = await (
        await page.request.get(baseURL + '/api/v1/insights/ai-availability')
      ).json()
      expect(cached.budget.userRemaining).toBe(after.budget.userRemaining)
    }
    const summary: components['schemas']['InsightSummary'] = await (
      await request.get('/api/v1/insights/summary')
    ).json()
    expect(summary.totalFindings).toBe(baseline.totalFindings)
    await page
      .getByRole('link', { name: new RegExp(shipment.shipmentNumber) })
      .first()
      .click()
    await expect(page).toHaveURL(`/shipments/${shipment.id}`)
    await page
      .getByRole('link', { name: 'Operational insights for this shipment' })
      .click()
    await expect(page).toHaveURL(new RegExp(`shipmentId=${shipment.id}`))
  } finally {
    await own.close()
  }
})

test('unauthenticated insight access and unapproved-account AI requests are denied without hiding deterministic findings', async ({
  browser,
  page,
  request,
  playwright,
  baseURL,
}) => {
  const context = await browser.newContext({
    storageState: { cookies: [], origins: [] },
  })
  try {
    const anonymous = await context.newPage()
    await anonymous.goto('http://127.0.0.1:5174/insights')
    await expect(anonymous).toHaveURL(/\/login$/)
    expect((await anonymous.request.get('/api/v1/insights')).status()).toBe(401)
    const admin = await playwright.request.newContext({
      baseURL,
      storageState: await authenticatedState(playwright, baseURL!, 'Admin'),
    })
    try {
      const { token } = (await (
        await admin.get('/api/v1/auth/csrf')
      ).json()) as { token: string }
      const account = {
        email: randomBytes(10).toString('hex') + '@example.test',
        displayName: 'Unapproved AI Viewer',
        role: 'Viewer',
        password: 'Ff!7' + randomBytes(24).toString('hex'),
      }
      expect(
        (
          await admin.post('/api/v1/users', {
            data: account,
            headers: { 'X-CSRF-TOKEN': token },
          })
        ).status(),
      ).toBe(201)
      const initial = (await (
        await anonymous.request.get('/api/v1/auth/csrf')
      ).json()) as { token: string }
      expect(
        (
          await anonymous.request.post('/api/v1/auth/login', {
            data: { email: account.email, password: account.password },
            headers: { 'X-CSRF-TOKEN': initial.token },
          })
        ).status(),
      ).toBe(200)
      const current = (await (
        await anonymous.request.get('/api/v1/auth/csrf')
      ).json()) as { token: string }
      expect((await anonymous.request.get('/api/v1/insights')).status()).toBe(
        200,
      )
      expect(
        (
          await anonymous.request.post('/api/v1/insights/ai-analysis', {
            data: { snapshotFingerprint: 'a'.repeat(64) },
            headers: { 'X-CSRF-TOKEN': current.token },
          })
        ).status(),
      ).toBe(403)
      await anonymous.goto(baseURL + '/insights')
      await expect(
        anonymous.getByRole('button', { name: 'Generate AI Analysis' }),
      ).toBeDisabled()
      await expect(
        anonymous.getByText(/not explicitly authorized for external AI/),
      ).toBeVisible()
    } finally {
      await admin.dispose()
    }
  } finally {
    await context.close()
  }
  await page.goto('/insights')
  await expect(
    page.getByRole('heading', { name: 'Prioritized operational alerts' }),
  ).toBeVisible()
  // A normal real authenticated request still needs CSRF. No test authentication scheme is installed.
  const noCsrf = await page.request.post('/api/v1/insights/ai-analysis', {
    data: { snapshotFingerprint: 'a'.repeat(64) },
  })
  expect(noCsrf.status()).toBe(400)
  const invalid = await request.post('/api/v1/insights/ai-analysis', {
    data: { snapshotFingerprint: 'invalid' },
  })
  expect(invalid.status()).toBe(400)
})
