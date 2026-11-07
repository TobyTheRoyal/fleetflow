import { resetCsrf } from '@/lib/api'
import { vi } from 'vitest'
export function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}
export function mockApi(
  handler: (path: string, options: RequestInit) => Response | Promise<Response>,
) {
  resetCsrf()
  const mock = vi.fn(async (url: string, options: RequestInit) =>
    url.endsWith('/api/v1/auth/csrf')
      ? json({ token: 'test-csrf-token' })
      : handler(url, options),
  )
  vi.stubGlobal('fetch', mock)
  return mock
}
