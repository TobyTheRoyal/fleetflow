import { test as base, expect } from '@playwright/test'
import { authenticatedState } from './auth'
export { expect }
export const test = base.extend({
  storageState: async ({ playwright, baseURL }, provide) => {
    await provide(await authenticatedState(playwright, baseURL!))
  },
  request: async ({ playwright, baseURL, storageState }, provide) => {
    const bootstrap = await playwright.request.newContext({
      baseURL,
      storageState,
    })
    const response = await bootstrap.get('/api/v1/auth/csrf')
    expect(response.ok()).toBeTruthy()
    const { token } = (await response.json()) as { token: string }
    const request = await playwright.request.newContext({
      baseURL,
      storageState: await bootstrap.storageState(),
      extraHTTPHeaders: { 'X-CSRF-TOKEN': token },
    })
    await bootstrap.dispose()
    await provide(request)
    await request.dispose()
  },
})
