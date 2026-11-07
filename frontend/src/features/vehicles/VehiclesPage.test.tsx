import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { VehiclesPage } from './VehiclesPage'
import { renderPage } from '@/test/render'
import { json, mockApi } from '@/test/http'
import { page, vehicle } from '@/test/fixtures'

afterEach(() => vi.unstubAllGlobals())

it('maps the actual API payload error code to vehicle capacity', async () => {
  mockApi((_, options) =>
    options.method === 'POST'
      ? json(
          { code: 'invalid_weight', detail: 'Maximum payload was rejected.' },
          422,
        )
      : json(page([vehicle])),
  )
  renderPage(<VehiclesPage />)
  await userEvent.click(screen.getByRole('button', { name: 'Add vehicle' }))
  await userEvent.type(screen.getByLabelText('Registration plate'), 'NEW-PLATE')
  await userEvent.click(screen.getByRole('button', { name: 'Create record' }))
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Maximum payload was rejected.',
  )
  expect(screen.getByLabelText('Maximum payload (kg)')).toHaveAttribute(
    'aria-invalid',
    'true',
  )
})

it('retains the persisted vehicle when reservation rules reject an availability edit', async () => {
  mockApi((_, options) =>
    options.method === 'PUT'
      ? json(
          {
            code: 'resource_in_use',
            detail: 'This vehicle has an active reservation.',
          },
          409,
        )
      : json(page([vehicle])),
  )
  renderPage(<VehiclesPage />)
  const row = await screen.findByRole('row', { name: /W-DEMO/ })
  await userEvent.click(within(row).getByRole('button', { name: 'Edit' }))
  await userEvent.selectOptions(
    screen.getByLabelText('Availability'),
    'Maintenance',
  )
  await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'active reservation',
  )
  expect(within(row).getByText('Available')).toBeInTheDocument()
  expect(screen.getByRole('dialog')).toBeVisible()
})
