import { SessionContext } from '@/features/auth/context'
import { type ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { render } from '@testing-library/react'
export function renderPage(ui: ReactNode, path = '/') {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  })
  function Location() {
    const value = useLocation()
    return (
      <output data-testid="location">
        {value.pathname}
        {value.search}
      </output>
    )
  }
  return {
    client,
    ...render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={[path]}>
          <SessionContext.Provider
            value={{
              user: {
                id: '11111111-1111-4111-8111-111111111111',
                email: 'dispatcher@example.test',
                displayName: 'Dispatcher',
                role: 'Dispatcher',
                active: true,
              },
              ready: true,
              locked: false,
              signedIn: () => {},
              logout: async () => {},
            }}
          >
            {ui}
          </SessionContext.Provider>
          <Location />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  }
}
