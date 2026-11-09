import { afterEach, expect, it, vi } from 'vitest'
import { verifyCurrentUser } from './verify-session'
import { mockApi, json } from '@/test/http'
const user = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'demo@example.test',
  displayName: 'Demo',
  role: 'Dispatcher',
  active: true,
}
afterEach(() => vi.unstubAllGlobals())
it('accepts the current cookie account without interrupting a workspace', async () => {
  mockApi(() => json(user))
  const events = vi.spyOn(window, 'dispatchEvent')
  await verifyCurrentUser(user.id)
  expect(events).not.toHaveBeenCalled()
})
it('rejects a reused demo account whose lease changed in another tab', async () => {
  mockApi(() =>
    json({
      ...user,
      demo: {
        leaseId: '33333333-3333-4333-8333-333333333333',
        mode: 'Dispatcher',
        expiresAtUtc: new Date(Date.now() + 1800000).toISOString(),
      },
    }),
  )
  await expect(
    verifyCurrentUser(
      user.id,
      undefined,
      '22222222-2222-4222-8222-222222222222',
    ),
  ).rejects.toMatchObject({ code: 'account_changed', status: 401 })
})
it('locks a workspace when another tab changes its cookie account', async () => {
  mockApi(() => json(user))
  const events = vi.spyOn(window, 'dispatchEvent')
  await expect(
    verifyCurrentUser('22222222-2222-4222-8222-222222222222'),
  ).rejects.toMatchObject({ code: 'account_changed', status: 401 })
  expect(events).toHaveBeenCalledWith(
    expect.objectContaining({ type: 'fleetflow:unauthorized' }),
  )
})
it('obtains a fresh CSRF token before reauthentication after a cookie account change', async () => {
  const { request, resetCsrf } = await import('@/lib/api')
  const { z } = await import('zod')
  resetCsrf()
  const calls = mockApi(() => json(user))
  await request('/api/v1/auth/login', z.unknown(), { method: 'POST' })
  await expect(
    verifyCurrentUser('22222222-2222-4222-8222-222222222222'),
  ).rejects.toMatchObject({ code: 'account_changed' })
  await request('/api/v1/auth/login', z.unknown(), { method: 'POST' })
  expect(
    calls.mock.calls.filter(([url]) => url.endsWith('/api/v1/auth/csrf')),
  ).toHaveLength(2)
  resetCsrf()
})
