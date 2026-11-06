import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

function renderApp() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  })
  return render(
    <QueryClientProvider client={client}>
      <App />
    </QueryClientProvider>,
  )
}

afterEach(() => vi.unstubAllGlobals())

describe('application connection', () => {
  it('announces loading before the dependency check completes', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise(() => {})),
    )
    renderApp()
    expect(screen.getByRole('status')).toHaveTextContent(
      'Checking API and database connection',
    )
    expect(screen.getByRole('button', { name: 'Checking…' })).toBeDisabled()
  })

  it('shows a successful real API response', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          new Response(JSON.stringify({ status: 'Healthy' }), { status: 200 }),
        ),
    )
    renderApp()
    expect(
      await screen.findByText('API and PostgreSQL are connected.'),
    ).toBeVisible()
  })

  it('shows a dependency failure and allows retrying successfully', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            code: 'dependency_unavailable',
            detail: 'The database is unavailable.',
          }),
          { status: 503 },
        ),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ status: 'Healthy' }), { status: 200 }),
      )
    vi.stubGlobal('fetch', fetchMock)
    renderApp()
    expect(
      await screen.findByText('The database is unavailable.'),
    ).toBeVisible()
    await userEvent.click(
      screen.getByRole('button', { name: 'Check connection' }),
    )
    expect(
      await screen.findByText('API and PostgreSQL are connected.'),
    ).toBeVisible()
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('rejects an unexpected success payload instead of reporting a healthy connection', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          new Response(JSON.stringify({ status: 'Unknown' }), { status: 200 }),
        ),
    )
    renderApp()
    expect(
      await screen.findByText(
        'The API returned an unexpected health response.',
      ),
    ).toBeVisible()
  })
})
