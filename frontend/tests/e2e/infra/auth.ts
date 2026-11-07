import type { PlaywrightWorkerArgs } from '@playwright/test'
import { readFileSync, existsSync } from 'node:fs'
import { join } from 'node:path'
export async function authenticatedState(
  playwright: PlaywrightWorkerArgs['playwright'],
  baseURL: string,
  role = 'Dispatcher',
) {
  const directory = process.env.FLEETFLOW_E2E_AUTH_DIR
  if (!directory)
    throw new Error('Authenticated E2E setup was not initialized.')
  const path = join(directory, role + '.json')
  const client = await playwright.request.newContext({
    baseURL,
    storageState: existsSync(path) ? path : undefined,
  })
  try {
    const current = await client.get('/api/v1/auth/me')
    if (current.status() === 401) {
      const passwords = JSON.parse(
        readFileSync(join(directory, 'credentials.json'), 'utf8'),
      ) as Record<string, string>
      const csrf = (await (await client.get('/api/v1/auth/csrf')).json()) as {
        token: string
      }
      const login = await client.post('/api/v1/auth/login', {
        data: {
          email: `${role.toLowerCase()}@fleetflow.example`,
          password: passwords[role],
        },
        headers: { 'X-CSRF-TOKEN': csrf.token },
      })
      if (!login.ok()) throw new Error('Test reauthentication failed.')
      return await client.storageState({ path })
    }
    if (!current.ok()) throw new Error('Test session could not be verified.')
    return await client.storageState()
  } finally {
    await client.dispose()
  }
}
