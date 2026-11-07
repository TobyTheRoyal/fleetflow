import {
  test,
  expect,
  type Page,
  type TestInfo,
  type APIRequestContext,
} from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import type { Page as ApiPage, Shipment } from '../../src/types/api'
const tags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']
async function audit(page: Page, info: TestInfo) {
  if (!(await page.getByRole('dialog').count()))
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(page.getByLabel('Loading data')).toHaveCount(0)
  const result = await new AxeBuilder({ page }).withTags(tags).analyze()
  await info.attach('accessibility.json', {
    body: JSON.stringify({
      url: page.url(),
      violations: result.violations,
      incomplete: result.incomplete,
    }),
    contentType: 'application/json',
  })
  expect(
    result.violations.map((v) => ({
      id: v.id,
      nodes: v.nodes.map((n) => ({
        target: n.target,
        summary: n.failureSummary,
      })),
    })),
  ).toEqual([])
}
async function planned(request: APIRequestContext) {
  const response = await request.get(
    '/api/v1/shipments?status=Planned&pageSize=1',
  )
  expect(response.ok()).toBeTruthy()
  const records: ApiPage<Shipment> = await response.json()
  const sample = records.items[0]
  if (!sample)
    throw new Error('A seeded Planned shipment is required for the audit.')
  return `/shipments/${sample.id}`
}
// A fresh context per page/dialog makes failures precise and prevents leaked browser
// state from a previous audit. Each case uses read-only fictional seed records.
for (const [name, path] of [
  ['overview', '/'],
  ['shipments', '/shipments'],
  ['creation', '/shipments/new'],
  ['customers', '/customers'],
  ['drivers', '/drivers'],
  ['vehicles', '/vehicles'],
]) {
  test(`WCAG A/AA: ${name}`, async ({ page }, info) => {
    await page.goto(path!)
    await audit(page, info)
  })
}
test('WCAG A/AA: shipment detail', async ({ page, request }, info) => {
  await page.goto(await planned(request))
  await audit(page, info)
})
test('ARIA references support role locators and a complete accessibility scan', async ({
  page,
}, info) => {
  // Firefox/Playwright regression #43967: role locators and Axe read ARIA
  // references in different JavaScript worlds. Keep both operations covered.
  await page.setContent(`<!doctype html><html lang="en"><head><title>ARIA regression</title></head>
    <body><main><h1>Accessible actions</h1><span id="action-label">Save</span>
    <button aria-labelledby="action-label">Save</button></main></body></html>`)
  await expect(
    page.getByRole('button', { name: 'Save', exact: true }),
  ).toBeVisible()
  await audit(page, info)
  await page.getByRole('button', { name: 'Save', exact: true }).click()
})
for (const label of ['Change assignment', 'Start transit']) {
  test(`WCAG A/AA: ${label} dialog and focus recovery`, async ({
    page,
    request,
  }, info) => {
    await page.goto(await planned(request))
    await page.getByRole('button', { name: label, exact: true }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await audit(page, info)
    await page.keyboard.press('Escape')
    await expect(
      page.getByRole('button', { name: label, exact: true }),
    ).toBeFocused()
  })
}
test('WCAG A/AA: validation dialog and keyboard focus containment', async ({
  page,
}, info) => {
  await page.goto('/customers')
  await page.getByRole('button', { name: 'Add customer', exact: true }).click()
  await page.getByRole('button', { name: 'Create record' }).click()
  await expect(page.getByLabel('Company name')).toHaveAttribute(
    'aria-invalid',
    'true',
  )
  await expect(
    page.getByRole('button', { name: 'Create record', exact: true }),
  ).toBeEnabled()
  // Check the real hover state too; opacity-based primary hover colors previously
  // fell below AA contrast against a white dialog background in WebKit.
  await page.getByRole('button', { name: 'Create record', exact: true }).hover()
  await audit(page, info)
  for (let i = 0; i < 8; i++) {
    await page.keyboard.press('Tab')
    expect(
      await page
        .getByRole('dialog')
        .evaluate((el) => el.contains(document.activeElement)),
    ).toBe(true)
  }
  await page.keyboard.press('Escape')
  await expect(
    page.getByRole('button', { name: 'Add customer', exact: true }),
  ).toBeFocused()
})
test('dialogs release pointer interaction after closing and navigation', async ({
  page,
}) => {
  for (const [path, title] of [
    ['/customers', 'customer'],
    ['/drivers', 'driver'],
    ['/vehicles', 'vehicle'],
  ]) {
    await page.goto(path!)
    const trigger = page.getByRole('button', {
      name: `Add ${title}`,
      exact: true,
    })
    await trigger.click()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()
    await dialog
      .getByRole('button', { name: 'Close dialog', exact: true })
      .click()
    await expect(dialog).toBeHidden()
    await expect(trigger).toBeFocused()
    await expect(page.locator('body')).not.toHaveCSS('pointer-events', 'none')
    await trigger.click()
    await expect(dialog).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(dialog).toBeHidden()
    await expect(trigger).toBeFocused()
  }
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('link', { name: 'Shipments', exact: true })
    .click()
  await expect(
    page.getByRole('heading', { name: 'Shipments', exact: true }),
  ).toBeVisible()
})
test('responsive pages keep tables contained and filters have a recovery path', async ({
  page,
}) => {
  for (const width of [1440, 820, 390, 320]) {
    await page.setViewportSize({ width, height: 950 })
    for (const path of ['/', '/shipments', '/shipments/new', '/vehicles']) {
      await page.goto(path)
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      await expect(page.getByLabel('Loading data')).toHaveCount(0)
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
      ).toBe(true)
    }
  }
  await page.goto('/shipments?page=999&pageSize=20')
  await expect(
    page.getByText('No shipments on this page', { exact: true }),
  ).toBeVisible()
  await page.getByRole('button', { name: 'Go to first page' }).click()
  await expect(page).toHaveURL(/page=1(?:&|$)/)
  await expect(page.getByRole('table')).toBeVisible()
})
