import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { ShipmentTimeline } from './ShipmentTimeline'
import { renderPage } from '@/test/render'
import { json, mockApi } from '@/test/http'
import { event, shipment, page } from '@/test/fixtures'

afterEach(() => vi.unstubAllGlobals())

it('loads complete history through bounded pages without inventing entries', async () => {
  const entries = Array.from({ length: 21 }, (_, i) => ({
    ...event,
    id: `50000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}`,
    message: `Recorded event ${i + 1}`,
  }))
  const fetch = mockApi((path) => {
    const current = Number(
      new URL(path, 'http://local').searchParams.get('page'),
    )
    return json(
      page(entries.slice((current - 1) * 20, current * 20), {
        page: current,
        totalCount: 21,
        totalPages: 2,
      }),
    )
  })
  renderPage(<ShipmentTimeline id={shipment.id} />)
  await screen.findByText('Recorded event 20')
  expect(screen.getAllByRole('listitem')).toHaveLength(20)
  await userEvent.click(
    screen.getByRole('button', { name: 'Load more history (20 of 21)' }),
  )
  expect(await screen.findByText('Recorded event 21')).toBeVisible()
  expect(screen.getAllByRole('listitem')).toHaveLength(21)
  expect(fetch.mock.calls.map(([url]) => url)).toEqual([
    `/api/v1/shipments/${shipment.id}/events?page=1&pageSize=20`,
    `/api/v1/shipments/${shipment.id}/events?page=2&pageSize=20`,
  ])
  expect(
    screen.queryByRole('button', { name: /Load more history/ }),
  ).not.toBeInTheDocument()
})

it('retains existing history when a later page fails and permits retry', async () => {
  let nextAttempts = 0
  mockApi((path) => {
    const next = path.includes('page=2&')
    if (next && ++nextAttempts === 1)
      return json(
        {
          code: 'dependency_unavailable',
          detail: 'History is temporarily unavailable.',
        },
        503,
      )
    return json(
      page(
        [
          {
            ...event,
            id: next ? '50000000-0000-4000-8000-000000000002' : event.id,
            message: next ? 'Second entry' : 'First entry',
          },
        ],
        { page: next ? 2 : 1, totalCount: 2, totalPages: 2 },
      ),
    )
  })
  renderPage(<ShipmentTimeline id={shipment.id} />)
  await screen.findByText('First entry')
  await userEvent.click(
    screen.getByRole('button', { name: 'Load more history (1 of 2)' }),
  )
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'History is temporarily unavailable.',
  )
  expect(screen.getByText('First entry')).toBeVisible()
  await userEvent.click(
    screen.getByRole('button', { name: 'Load more history (1 of 2)' }),
  )
  expect(await screen.findByText('Second entry')).toBeVisible()
  await waitFor(() =>
    expect(screen.queryByRole('alert')).not.toBeInTheDocument(),
  )
})
