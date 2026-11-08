import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { expect, it, vi } from 'vitest'
import { SessionContext, type SessionUser } from '@/features/auth/context'
import { RealtimeStatus } from './RealtimeStatus'
import { manageConnection } from './connection'
vi.mock('./connection', () => ({
  manageConnection: vi.fn(() => vi.fn()),
  createConnection: vi.fn(),
}))
const user: SessionUser = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'demo@example.test',
  displayName: 'Demo',
  role: 'Dispatcher',
  active: true,
}
it('only connects for an authenticated unlocked session and cleans up on expiry', () => {
  const cleanup = vi.fn()
  vi.mocked(manageConnection).mockReturnValue(cleanup)
  const cache = new QueryClient()
  const view = (current: SessionUser | null, locked = false) => (
    <QueryClientProvider client={cache}>
      <SessionContext.Provider
        value={{
          user: current,
          locked,
          ready: true,
          signedIn: vi.fn(),
          logout: vi.fn(),
        }}
      >
        <RealtimeStatus />
      </SessionContext.Provider>
    </QueryClientProvider>
  )
  const component = render(view(null))
  expect(manageConnection).not.toHaveBeenCalled()
  component.rerender(view(user))
  expect(manageConnection).toHaveBeenCalledOnce()
  component.rerender(view(user, true))
  expect(cleanup).toHaveBeenCalledOnce()
  expect(
    screen.getByRole('status', { name: 'Real-time: Disconnected' }),
  ).toBeVisible()
})
it('replaces connections for account changes, cleans up on logout and preserves the status label', () => {
  const cleanup = vi.fn()
  vi.mocked(manageConnection).mockImplementation((_cache, status) => {
    status('Connected')
    return cleanup
  })
  const cache = new QueryClient()
  const view = (current: SessionUser | null) => (
    <QueryClientProvider client={cache}>
      <SessionContext.Provider
        value={{
          user: current,
          locked: false,
          ready: true,
          signedIn: vi.fn(),
          logout: vi.fn(),
        }}
      >
        <RealtimeStatus />
      </SessionContext.Provider>
    </QueryClientProvider>
  )
  const component = render(view(user))
  expect(
    screen.getByRole('status', { name: 'Real-time: Connected' }),
  ).toBeVisible()
  component.rerender(
    view({ ...user, id: '22222222-2222-4222-8222-222222222222' }),
  )
  expect(cleanup).toHaveBeenCalledOnce()
  component.rerender(view(null))
  expect(cleanup).toHaveBeenCalledTimes(2)
  expect(
    screen.getByRole('status', { name: 'Real-time: Disconnected' }),
  ).toBeVisible()
})
