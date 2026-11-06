import { test, expect } from '@playwright/test'

test('the browser connects through Vite to the real API and PostgreSQL', async ({
  page,
}) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(page.getByRole('status')).toHaveText(
    'API and PostgreSQL are connected.',
  )
  const response = await page.request.get('/openapi/v1.json')
  expect(response.ok()).toBeTruthy()
  await page.getByRole('button', { name: 'Check connection' }).click()
  await expect(page.getByRole('status')).toHaveText(
    'API and PostgreSQL are connected.',
  )
})
