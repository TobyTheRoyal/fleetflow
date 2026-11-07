import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { ShipmentsPage } from './ShipmentsPage'
import { renderPage } from '@/test/render'
import { json, mockApi } from '@/test/http'
import { customer, shipment, page } from '@/test/fixtures'
afterEach(() => vi.unstubAllGlobals())
it('keeps filtering and pagination on the server and in the URL', async () => {
  const fetch = mockApi((path) =>
    json(
      path.includes('customers')
        ? page([customer])
        : page([shipment], {
            page: Number(
              new URL(path, 'http://local').searchParams.get('page') ?? 1,
            ),
            totalCount: 45,
            totalPages: 3,
          }),
    ),
  )
  renderPage(<ShipmentsPage />)
  await screen.findByRole('link', {
    name: shipment.shipmentNumber,
  })
  await userEvent.type(screen.getByLabelText('Search shipments'), 'Vienna')
  await userEvent.selectOptions(
    screen.getByLabelText('Filter by status'),
    'Planned',
  )
  await userEvent.selectOptions(
    screen.getByLabelText('Filter by customer'),
    customer.id,
  )
  await userEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
  await waitFor(() =>
    expect(
      fetch.mock.calls.some(
        ([url]) =>
          url.includes('status=Planned') &&
          url.includes('search=Vienna') &&
          url.includes(customer.id),
      ),
    ).toBe(true),
  )
  expect(screen.getByTestId('location')).toHaveTextContent('status=Planned')
  await userEvent.click(screen.getByRole('button', { name: 'Next page' }))
  await waitFor(() =>
    expect(fetch.mock.calls.some(([url]) => url.includes('page=2'))).toBe(true),
  )
  await userEvent.selectOptions(screen.getByLabelText('Rows per page'), '50')
  await waitFor(() =>
    expect(
      fetch.mock.calls.some(
        ([url]) => url.includes('pageSize=50') && url.includes('page=1'),
      ),
    ).toBe(true),
  )
})
it('offers clearing filters for an empty server result', async () => {
  mockApi(() => json(page([])))
  renderPage(<ShipmentsPage />, '/shipments?status=Delivered')
  expect(await screen.findByText('No matching shipments')).toBeVisible()
  expect(screen.getByRole('button', { name: 'Clear filters' })).toBeVisible()
})
