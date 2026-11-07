import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SystemHealth } from '@/components/layout/SystemHealth'
import { renderPage } from '@/test/render'
import { mockApi, json } from '@/test/http'
afterEach(() => vi.unstubAllGlobals())
describe('application connection', () => {
  it('announces loading until dependency check completes', () => {
    mockApi(() => new Promise(() => {}))
    renderPage(<SystemHealth />)
    expect(screen.getByTestId('connection-status')).toHaveTextContent(
      'Checking API and database connection',
    )
    expect(
      screen.getByRole('button', { name: 'Checking…', hidden: true }),
    ).toBeDisabled()
  })
  it('shows a healthy API response', async () => {
    mockApi(() => json({ status: 'Healthy' }))
    renderPage(<SystemHealth />)
    expect(await screen.findByTestId('connection-status')).toBeInTheDocument()
    await screen.findByText('Systems operational')
    expect(screen.getByTestId('connection-status')).toHaveTextContent(
      'API and PostgreSQL are connected.',
    )
  })
  it('shows dependency failure and allows explicit successful retry', async () => {
    let attempts = 0
    mockApi(() =>
      ++attempts === 1
        ? json(
            {
              code: 'dependency_unavailable',
              detail: 'The database is unavailable.',
            },
            503,
          )
        : json({ status: 'Healthy' }),
    )
    renderPage(<SystemHealth />)
    await screen.findByText('Connection issue')
    await userEvent.click(screen.getByText('Connection issue'))
    expect(screen.getByTestId('connection-status')).toHaveTextContent(
      'The database is unavailable.',
    )
    await userEvent.click(
      screen.getByRole('button', { name: 'Check connection' }),
    )
    await screen.findByText('Systems operational')
    expect(attempts).toBe(2)
  })
  it('rejects an unexpected health response', async () => {
    mockApi(() => json({ status: 'Unknown' }))
    renderPage(<SystemHealth />)
    await screen.findByText('Connection issue')
    expect(screen.getByTestId('connection-status')).toHaveTextContent(
      'unexpected response',
    )
  })
})
