import { afterEach, expect, it, vi } from 'vitest'
import { render, screen, act } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { z } from 'zod'
import { SessionProvider, ProtectedRoute } from './session'
import { useSession } from './context'
import { LoginPage } from './LoginForm'
import { mockApi, json } from '@/test/http'
import { request } from '@/lib/api'
const user = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'dispatcher@example.test',
  displayName: 'Demo Dispatcher',
  role: 'Dispatcher',
  active: true,
}
const denied = () =>
  json({ code: 'authentication_required', detail: 'Sign in to continue.' }, 401)
afterEach(() => vi.unstubAllGlobals())
function mount(child: React.ReactNode, path = '/') {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <SessionProvider>{child}</SessionProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return client
}
function Workspace() {
  const { user, logout } = useSession()
  return (
    <>
      <h1>{user?.displayName ?? 'Anonymous'}</h1>
      <input aria-label="Unsaved cargo" defaultValue="" />
      <button
        onClick={() => {
          void logout()
        }}
      >
        Sign out
      </button>
    </>
  )
}
it('restores an authenticated session and logout clears cached server data', async () => {
  mockApi((path) =>
    path.endsWith('/logout') ? json({ success: true }) : json(user),
  )
  const client = mount(<Workspace />)
  await screen.findByRole('heading', { name: user.displayName })
  client.setQueryData(['shipments'], ['private cached row'])
  await userEvent.click(screen.getByRole('button', { name: 'Sign out' }))
  await screen.findByRole('heading', { name: 'Anonymous' })
  expect(client.getQueryData(['shipments'])).toBeUndefined()
})
it('signs in and restores the workspace route', async () => {
  let authenticated = false
  mockApi((path) => {
    if (path.endsWith('/login')) authenticated = true
    return authenticated ? json(user) : denied()
  })
  mount(
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<Workspace />} />
    </Routes>,
    '/login',
  )
  await userEvent.type(screen.getByLabelText('Email'), user.email)
  await userEvent.type(screen.getByLabelText('Password'), 'Sample-password12!')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
  await screen.findByRole('heading', { name: user.displayName })
})
it('failed login preserves email, clears the password and shows a safe error', async () => {
  mockApi(() => denied())
  mount(<LoginPage />)
  await userEvent.type(screen.getByLabelText('Email'), user.email)
  await userEvent.type(screen.getByLabelText('Password'), 'Wrong-password12!')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
  await screen.findByRole('alert')
  expect(screen.getByLabelText('Email')).toHaveValue(user.email)
  expect(screen.getByLabelText('Password')).toHaveValue('')
})
it('viewer cannot reach a write route', async () => {
  mockApi(() => json({ ...user, role: 'Viewer' }))
  mount(
    <Routes>
      <Route element={<ProtectedRoute write />}>
        <Route
          path="/shipments/new"
          element={<button>Create shipment</button>}
        />
      </Route>
    </Routes>,
    '/shipments/new',
  )
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Your role does not permit',
  )
  expect(
    screen.queryByRole('button', { name: 'Create shipment' }),
  ).not.toBeInTheDocument()
})
it('401 preserves unsaved data and same-user reauthentication resumes the form', async () => {
  mockApi((path) => (path.includes('/shipments') ? denied() : json(user)))
  mount(<Workspace />)
  await screen.findByRole('heading', { name: user.displayName })
  await userEvent.type(
    screen.getByLabelText('Unsaved cargo'),
    'Fragile machinery',
  )
  await act(async () => {
    await request('/api/v1/shipments', z.object({})).catch(() => {})
  })
  await screen.findByRole('dialog', { name: 'Session expired' })
  await userEvent.type(screen.getByLabelText('Password'), 'Sample-password12!')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(screen.getByLabelText('Unsaved cargo')).toHaveValue(
    'Fragile machinery',
  )
})
it('403 does not turn an authorization failure into a session-expired dialog', async () => {
  mockApi((path) =>
    path.includes('/users')
      ? json({ code: 'forbidden', detail: 'Read only.' }, 403)
      : json(user),
  )
  mount(<Workspace />)
  await screen.findByRole('heading', { name: user.displayName })
  await act(async () => {
    await expect(request('/api/v1/users', z.object({}))).rejects.toMatchObject({
      status: 403,
    })
  })
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
})

it('waits for session restoration before accepting a login submission', () => {
  mockApi(() => new Promise(() => {}))
  mount(<LoginPage />)
  expect(
    screen.getByRole('button', { name: 'Checking session…' }),
  ).toBeDisabled()
})
