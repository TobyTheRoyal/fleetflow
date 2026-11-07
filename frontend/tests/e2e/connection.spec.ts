import { test, expect } from './infra/test'
test('the browser connects through Vite to the real API and PostgreSQL', async ({
  page,
}) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(
    'Operations overview',
  )
  await expect(page.getByTestId('connection-status')).toHaveText(
    'API and PostgreSQL are connected.',
  )
  expect((await page.request.get('/openapi/v1.json')).ok()).toBeTruthy()
  await page.locator('.health-details summary').click()
  await page.getByRole('button', { name: 'Check connection' }).click()
  await expect(page.getByTestId('connection-status')).toHaveText(
    'API and PostgreSQL are connected.',
  )
})
