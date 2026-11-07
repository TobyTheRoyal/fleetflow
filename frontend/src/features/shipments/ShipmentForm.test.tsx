import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { ShipmentForm } from './ShipmentForm'
import { renderPage } from '@/test/render'
import { json, mockApi } from '@/test/http'
import { customer, shipment, page } from '@/test/fixtures'
afterEach(() => vi.unstubAllGlobals())
it('validates required shipment data before submitting', async () => {
  const fetch = mockApi(() => json(page([customer])))
  renderPage(<ShipmentForm />)
  await userEvent.click(screen.getByRole('button', { name: 'Create shipment' }))
  expect(await screen.findByText('Select a customer.')).toBeVisible()
  expect(screen.getByLabelText('Weight (kg)')).toHaveAttribute(
    'aria-invalid',
    'true',
  )
  expect(
    fetch.mock.calls.every(([, options]) => options.method === 'GET'),
  ).toBe(true)
})
it('maps backend validation to a field and retains the entered values', async () => {
  mockApi((_, opts) =>
    opts.method === 'PUT'
      ? json(
          { code: 'invalid_weight', detail: 'Weight rejected by the server.' },
          422,
        )
      : json(page([customer])),
  )
  renderPage(<ShipmentForm initial={shipment} />)
  await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Weight rejected by the server.',
  )
  expect(screen.getByLabelText('Weight (kg)')).toHaveAttribute(
    'aria-invalid',
    'true',
  )
  expect(screen.getByLabelText('Origin city')).toHaveValue('Vienna')
})
it('locks writes on a stale version until an explicit reload', async () => {
  const fetch = mockApi((path, opts) =>
    opts.method === 'PUT'
      ? json({ code: 'stale_version', detail: 'Stale version.' }, 409)
      : path.includes('customers')
        ? json(page([customer]))
        : json({
            ...shipment,
            version: 3,
            cargoDescription: 'Updated elsewhere',
          }),
  )
  renderPage(<ShipmentForm initial={shipment} />)
  await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Shipment changed elsewhere',
  )
  expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled()
  await userEvent.click(
    screen.getByRole('button', { name: 'Discard edits and reload latest' }),
  )
  expect(await screen.findByDisplayValue('Updated elsewhere')).toBeVisible()
  expect(screen.getByRole('button', { name: 'Save changes' })).toBeEnabled()
  expect(
    fetch.mock.calls.filter(([, opts]) => opts.method === 'PUT'),
  ).toHaveLength(1)
})
it('offers only cargo description edits for Planned shipments', () => {
  mockApi(() => json(page([customer])))
  renderPage(<ShipmentForm initial={{ ...shipment, status: 'Planned' }} />)
  expect(screen.queryByLabelText('Weight (kg)')).not.toBeInTheDocument()
  expect(screen.getByLabelText('Cargo description (optional)')).toBeVisible()
})

it('keeps a stale form blocked when reloading fails', async () => {
  mockApi((path, options) =>
    options.method === 'PUT'
      ? json({ code: 'stale_version', detail: 'Changed elsewhere.' }, 409)
      : path.includes('customers')
        ? json(page([customer]))
        : json(
            { code: 'dependency_unavailable', detail: 'Database unavailable.' },
            503,
          ),
  )
  renderPage(<ShipmentForm initial={shipment} />)
  await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
  await screen.findByText('Shipment changed elsewhere')
  await userEvent.click(
    screen.getByRole('button', { name: 'Discard edits and reload latest' }),
  )
  expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled()
  expect(screen.getByRole('alert')).toHaveTextContent(
    'Shipment changed elsewhere',
  )
})
