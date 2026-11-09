import { afterEach, expect, it, vi } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { SessionProvider, ProtectedRoute } from '@/features/auth/session'
import { useSession } from '@/features/auth/context'
import { request } from '@/lib/api'
import { z } from 'zod'
import { mockApi, json } from '@/test/http'
import { PortfolioBootstrap } from './PortfolioBootstrap'
import { DemoPage } from './DemoPage'
import { DemoBanner } from './DemoBanner'
const availability = {
  enabled: true,
  viewerAvailable: 10,
  dispatcherAvailable: 3,
  leaseMinutes: 30,
}
const user = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'demo@example.test',
  displayName: 'Demo Dispatcher',
  role: 'Dispatcher' as const,
  active: true,
  demo: {
    leaseId: '22222222-2222-4222-8222-222222222222',
    mode: 'Dispatcher' as const,
    expiresAtUtc: new Date(Date.now() + 1800000).toISOString(),
  },
}
const denied = () => json({ code: 'authentication_required' }, 401)
afterEach(() => vi.unstubAllGlobals())
function Workspace() {
  const session = useSession()
  return (
    <>
      <DemoBanner />
      <h1>{session.user?.displayName}</h1>
      <input aria-label="Unsaved cargo" />
      <button
        onClick={() =>
          session.signedIn({
            ...user,
            demo: {
              ...user.demo,
              leaseId: '33333333-3333-4333-8333-333333333333',
            },
          })
        }
      >
        Replace lease
      </button>
    </>
  )
}
function mount(path = '/demo') {
  const cache = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  })
  render(
    <QueryClientProvider client={cache}>
      <MemoryRouter initialEntries={[path]}>
        <SessionProvider portfolioMode>
          <Routes>
            <Route path="/demo" element={<DemoPage />} />
            <Route element={<ProtectedRoute />}>
              <Route path="/" element={<Workspace />} />
            </Route>
          </Routes>
        </SessionProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return cache
}
it('normal mode is discovered from a disabled endpoint without a demo login', async () => {
  mockApi(() => json({ code: 'http_error' }, 404))
  render(
    <PortfolioBootstrap>
      {(enabled) => <p>{enabled ? 'Portfolio' : 'Normal sign-in'}</p>}
    </PortfolioBootstrap>,
  )
  await screen.findByText('Normal sign-in')
})
it('bootstrap offers recovery when the sleeping service is unavailable', async () => {
  let available = false
  mockApi(() =>
    available
      ? json(availability)
      : json({ detail: 'Service unavailable' }, 503),
  )
  render(<PortfolioBootstrap>{() => <h1>Demo ready</h1>}</PortfolioBootstrap>)
  await screen.findByRole('alert')
  available = true
  await userEvent.click(screen.getByRole('button', { name: 'Try again' }))
  await screen.findByRole('heading', { name: 'Demo ready' })
})
it('shows real capacities and creates exactly one explicitly requested session', async () => {
  const fetch = mockApi((path) =>
    path.endsWith('/demo')
      ? json(availability)
      : path.endsWith('/sessions')
        ? json(user)
        : denied(),
  )
  mount()
  await screen.findByText('3 interactive sandboxes available')
  await userEvent.click(
    screen.getByRole('button', { name: 'Start Interactive Demo' }),
  )
  await screen.findByRole('heading', { name: 'Demo Dispatcher' })
  expect(
    fetch.mock.calls.filter(([url]) => url.endsWith('/sessions')),
  ).toHaveLength(1)
  expect(fetch.mock.calls.some(([url]) => url.includes('ai-analysis'))).toBe(
    false,
  )
})
it('disables exhausted demo modes and displays actionable entry rate limits', async () => {
  mockApi((path) =>
    path.endsWith('/demo')
      ? json({ ...availability, viewerAvailable: 0 })
      : path.endsWith('/sessions')
        ? json(
            {
              detail: 'Too many attempts. Try again later.',
              code: 'demo_entry_rate_limited',
            },
            429,
          )
        : denied(),
  )
  mount()
  await screen.findByText('0 read-only places available')
  expect(
    screen.getByRole('button', { name: 'Explore Read-Only Demo' }),
  ).toBeDisabled()
  await userEvent.click(
    screen.getByRole('button', { name: 'Start Interactive Demo' }),
  )
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Too many attempts',
  )
})
it('revokes the session only after confirming and returns to the entry page', async () => {
  mockApi((path) =>
    path.endsWith('/demo')
      ? json(availability)
      : path.endsWith('/logout')
        ? json({ success: true })
        : json(user),
  )
  const cache = mount('/')
  await screen.findByRole('heading', { name: user.displayName })
  cache.setQueryData(['shipments'], ['private'])
  expect(screen.getByLabelText('Remaining session time')).toBeVisible()
  await userEvent.click(screen.getByRole('button', { name: 'End demo' }))
  await screen.findByRole('dialog', { name: 'End this demo?' })
  await userEvent.click(
    screen.getByRole('button', { name: 'End demo and discard edits' }),
  )
  await screen.findByRole('heading', { name: 'A working logistics workspace.' })
  expect(cache.getQueryData(['shipments'])).toBeUndefined()
})
it('401 expires a demo immediately, removes edits and never offers password reauthentication', async () => {
  mockApi((path) =>
    path.endsWith('/demo')
      ? json(availability)
      : path.endsWith('/shipments')
        ? denied()
        : json(user),
  )
  const cache = mount('/')
  await screen.findByRole('heading', { name: user.displayName })
  await userEvent.type(
    screen.getByLabelText('Unsaved cargo'),
    'Private old lease',
  )
  cache.setQueryData(['shipments'], ['private'])
  await act(async () => {
    await request('/api/v1/shipments', z.object({})).catch(() => {})
  })
  await screen.findByRole('heading', { name: 'A working logistics workspace.' })
  expect(screen.queryByLabelText('Unsaved cargo')).not.toBeInTheDocument()
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(cache.getQueryData(['shipments'])).toBeUndefined()
})
it('same pooled account with a new lease cannot reuse cached data or local form state', async () => {
  mockApi(() => json(user))
  const cache = mount('/')
  await screen.findByRole('heading', { name: user.displayName })
  await userEvent.type(screen.getByLabelText('Unsaved cargo'), 'Old visitor')
  cache.setQueryData(['customers'], ['private'])
  await userEvent.click(screen.getByRole('button', { name: 'Replace lease' }))
  expect(screen.getByLabelText('Unsaved cargo')).toHaveValue('')
  expect(cache.getQueryData(['customers'])).toBeUndefined()
})
it('a restored lease past its deadline expires locally with an understandable explanation', async () => {
  mockApi((path) =>
    path.endsWith('/demo')
      ? json(availability)
      : json({
          ...user,
          demo: {
            ...user.demo,
            expiresAtUtc: new Date(Date.now() - 1000).toISOString(),
          },
        }),
  )
  mount('/')
  await screen.findByRole('heading', { name: 'A working logistics workspace.' })
  expect(
    screen.getByText(/Your temporary demo session has ended/),
  ).toBeVisible()
})
