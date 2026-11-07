import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { AssignmentDialog, StatusDialog } from './ShipmentActions'
import { ShipmentDetailPage } from './ShipmentDetailPage'
import { Routes, Route } from 'react-router-dom'
import { renderPage } from '@/test/render'
import { json, mockApi } from '@/test/http'
import {
  shipment,
  assigned,
  driver,
  vehicle,
  event,
  page,
} from '@/test/fixtures'
afterEach(() => vi.unstubAllGlobals())
it('assigns eligible resources with the original version, keeping ineligible options visible', async () => {
  const close = vi.fn()
  const fetch = mockApi((path, opts) =>
    opts.method === 'POST'
      ? json(assigned)
      : json(
          path.includes('drivers')
            ? page([
                driver,
                {
                  ...driver,
                  id: '20000000-0000-4000-8000-000000000002',
                  active: false,
                  lastName: 'Inactive',
                },
              ])
            : page([
                vehicle,
                {
                  ...vehicle,
                  id: '30000000-0000-4000-8000-000000000002',
                  plateNumber: 'TOO-SMALL',
                  maxPayloadKg: 100,
                },
              ]),
        ),
  )
  renderPage(<AssignmentDialog shipment={shipment} close={close} />)
  await screen.findByRole('option', { name: /Alex Demo/ })
  expect(screen.getByRole('option', { name: /Inactive/ })).toBeDisabled()
  expect(
    screen.getByRole('option', { name: /Insufficient payload/ }),
  ).toBeDisabled()
  await userEvent.selectOptions(screen.getByLabelText('Driver', {}), driver.id)
  await userEvent.selectOptions(
    screen.getByLabelText('Vehicle', {}),
    vehicle.id,
  )
  await userEvent.click(screen.getByRole('button', { name: 'Save assignment' }))
  expect(close).toHaveBeenCalledOnce()
  expect(fetch.mock.calls.find(([, o]) => o.method === 'POST')?.[1]?.body).toBe(
    JSON.stringify({ driverId: driver.id, vehicleId: vehicle.id, version: 1 }),
  )
})
it.each([
  ['assignment_overlap', 409, 'Driver already reserved during this time.'],
  ['vehicle_overloaded', 422, 'Cargo exceeds vehicle payload.'],
  ['driver_inactive', 409, 'Driver is inactive.'],
  ['vehicle_unavailable', 409, 'Vehicle is unavailable.'],
])('shows %s from the authoritative backend', async (code, status, detail) => {
  mockApi((path, opts) =>
    opts.method === 'POST'
      ? json({ code, detail }, status)
      : json(path.includes('drivers') ? page([driver]) : page([vehicle])),
  )
  renderPage(<AssignmentDialog shipment={assigned} close={vi.fn()} />)
  await screen.findByRole('option', { name: /Alex Demo/ })
  await userEvent.click(screen.getByRole('button', { name: 'Save assignment' }))
  expect(await screen.findByRole('alert')).toHaveTextContent(detail)
})
it('posts a confirmed transition and optional reason using the expected version', async () => {
  const close = vi.fn()
  const fetch = mockApi(() =>
    json({ ...assigned, status: 'Planned', version: 3 }),
  )
  renderPage(
    <StatusDialog shipment={assigned} target="Planned" close={close} />,
  )
  await userEvent.type(
    screen.getByLabelText('Reason (optional)'),
    'Ready to dispatch',
  )
  await userEvent.click(screen.getByRole('button', { name: 'Plan shipment' }))
  expect(close).toHaveBeenCalledOnce()
  expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toEqual({
    version: 2,
    status: 'Planned',
    reason: 'Ready to dispatch',
  })
})
it('shows rejected transitions without closing or adding local timeline events', async () => {
  const close = vi.fn()
  mockApi(() =>
    json(
      {
        code: 'invalid_transition',
        detail: 'This transition is no longer valid.',
      },
      409,
    ),
  )
  renderPage(
    <StatusDialog shipment={assigned} target="Planned" close={close} />,
  )
  await userEvent.click(screen.getByRole('button', { name: 'Plan shipment' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('no longer valid')
  expect(close).not.toHaveBeenCalled()
})
it('disables stale status retries and requires deliberate reload', async () => {
  const close = vi.fn()
  const fetch = mockApi(() =>
    json({ code: 'stale_version', detail: 'Stale shipment.' }, 409),
  )
  renderPage(
    <StatusDialog shipment={assigned} target="Planned" close={close} />,
  )
  await userEvent.click(screen.getByRole('button', { name: 'Plan shipment' }))
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Shipment changed elsewhere',
  )
  expect(screen.getByRole('button', { name: 'Plan shipment' })).toBeDisabled()
  expect(fetch).toHaveBeenCalledTimes(1)
  await userEvent.click(
    screen.getByRole('button', { name: 'Discard edits and reload latest' }),
  )
  expect(close).toHaveBeenCalledOnce()
})
it.each(['Draft', 'Planned', 'InTransit', 'Delivered', 'Cancelled'] as const)(
  'shows only contextual actions and actual history for %s',
  async (status) => {
    mockApi((path) =>
      json(
        path.endsWith('/events?page=1&pageSize=20')
          ? page([event])
          : { ...assigned, status },
      ),
    )
    renderPage(
      <Routes>
        <Route path="/shipments/:id" element={<ShipmentDetailPage />} />
      </Routes>,
      `/shipments/${shipment.id}`,
    )
    await screen.findByRole('heading', { name: shipment.shipmentNumber })
    expect(
      await screen.findByRole('heading', { name: 'Shipment created' }),
    ).toBeVisible()
    const actions = within(
      screen.getByText('SHIPMENT DETAILS').parentElement!.parentElement!,
    )
    expect(
      actions.queryByRole('link', { name: 'Edit shipment' }) !== null,
    ).toBe(['Draft', 'Planned'].includes(status))
    if (status === 'Delivered' || status === 'Cancelled') {
      expect(
        screen.queryByRole('button', { name: 'Start transit' }),
      ).not.toBeInTheDocument()
      expect(
        screen.queryByRole('button', { name: 'Mark delivered' }),
      ).not.toBeInTheDocument()
    }
  },
)
