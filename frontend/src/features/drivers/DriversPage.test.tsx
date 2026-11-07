import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { DriversPage } from './DriversPage'
import { renderPage } from '@/test/render'
import { json, mockApi } from '@/test/http'
import { driver, page } from '@/test/fixtures'
afterEach(() => vi.unstubAllGlobals())
it('keeps the saved driver active when the backend rejects deactivation', async () => {
  mockApi((_, options) =>
    options.method === 'PUT'
      ? json(
          {
            code: 'resource_in_use',
            detail: 'The driver is assigned to an active shipment.',
          },
          409,
        )
      : json(page([driver])),
  )
  renderPage(<DriversPage />)
  const row = await screen.findByRole('row', { name: /Alex Demo/ })
  await userEvent.click(within(row).getByRole('button', { name: 'Edit' }))
  await userEvent.click(screen.getByRole('checkbox', { name: /Active driver/ }))
  await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('active shipment')
  expect(within(row).getByText('Active')).toBeVisible()
  expect(screen.getByRole('dialog')).toBeVisible()
})
