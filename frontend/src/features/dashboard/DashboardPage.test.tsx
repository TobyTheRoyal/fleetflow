import { screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { DashboardPage } from './DashboardPage'
import { renderPage } from '@/test/render'
import { json, mockApi } from '@/test/http'
import { dashboard, page } from '@/test/fixtures'
afterEach(() => vi.unstubAllGlobals())
it('renders actual KPI counts, definitions and recent shipments', async () => {
  mockApi((path) => json(path.includes('summary') ? dashboard : page([])))
  renderPage(<DashboardPage />)
  const card = await screen.findByRole('link', { name: /Total shipments/ })
  expect(within(card).getByText('25')).toBeVisible()
  expect(
    screen.getByText(
      'Delivered shipments are included in the total. Cancelled shipments are excluded.',
    ),
  ).toBeVisible()
  expect(
    screen.getByRole('link', { name: /Due within 24 hours/ }),
  ).toHaveAttribute('href', '/shipments?deadline=DueSoon')
  expect(screen.getByRole('link', { name: 'FF-2026-000001' })).toBeVisible()
})
it('shows skeletons while loading', () => {
  mockApi(() => new Promise(() => {}))
  renderPage(<DashboardPage />)
  expect(screen.getByLabelText('Loading data')).toBeVisible()
  expect(screen.queryByText('25')).not.toBeInTheDocument()
})
it('shows an actionable service failure', async () => {
  mockApi(() =>
    json(
      {
        detail: 'Database is temporarily unavailable.',
        code: 'dependency_unavailable',
      },
      503,
    ),
  )
  renderPage(<DashboardPage />)
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Database is temporarily unavailable.',
  )
  expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible()
})
it('offers first shipment creation for empty real results', async () => {
  mockApi((path) =>
    json(
      path.includes('summary')
        ? { ...dashboard, recentShipments: [] }
        : page([]),
    ),
  )
  renderPage(<DashboardPage />)
  expect(await screen.findByText('Your operation starts here')).toBeVisible()
  expect(screen.getByRole('link', { name: 'Create shipment' })).toHaveAttribute(
    'href',
    '/shipments/new',
  )
})
