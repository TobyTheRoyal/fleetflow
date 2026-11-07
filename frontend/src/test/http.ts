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
  const mock = vi.fn((url: string, options: RequestInit) =>
    handler(url, options),
  )
  vi.stubGlobal('fetch', mock)
  return mock
}
