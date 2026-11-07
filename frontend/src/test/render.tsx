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
          {ui}
          <Location />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  }
}
