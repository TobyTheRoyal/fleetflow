import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { CustomersPage } from './CustomersPage'
import { renderPage } from '@/test/render'
import { json, mockApi } from '@/test/http'
import { customer, page } from '@/test/fixtures'
afterEach(() => vi.unstubAllGlobals())
it('validates contact fields before posting and maps backend field errors', async () => {
  const fetch = mockApi((_, options) =>
    options.method === 'POST'
      ? json(
          {
            code: 'invalid_request',
            errors: { ContactName: ['Contact must be corrected.'] },
          },
          400,
        )
      : json(page([customer])),
  )
  renderPage(<CustomersPage />)
  await userEvent.click(screen.getByRole('button', { name: 'Add customer' }))
  await userEvent.click(screen.getByRole('button', { name: 'Create record' }))
  expect(await screen.findByText('Company name is required.')).toBeVisible()
  expect(
    fetch.mock.calls.every(([, options]) => options.method === 'GET'),
  ).toBe(true)
  await userEvent.type(screen.getByLabelText('Company name'), 'Fictional Demo')
  await userEvent.type(screen.getByLabelText('Contact name'), 'Taylor')
  await userEvent.type(screen.getByLabelText('Email'), 'bad email')
  await userEvent.click(screen.getByRole('button', { name: 'Create record' }))
  expect(await screen.findByText('Enter a valid email address.')).toBeVisible()
  await userEvent.clear(screen.getByLabelText('Email'))
  await userEvent.type(screen.getByLabelText('Email'), 'demo@example.test')
  await userEvent.click(screen.getByRole('button', { name: 'Create record' }))
  expect(await screen.findByText('Contact must be corrected.')).toBeVisible()
  expect(screen.getByLabelText('Contact name')).toHaveAttribute(
    'aria-invalid',
    'true',
  )
})
it('distinguishes an empty out-of-range page from an empty directory', async () => {
  mockApi((path) =>
    json(
      path.includes('page=999&')
        ? page([], { page: 999, totalCount: 1, totalPages: 1 })
        : page([customer]),
    ),
  )
  renderPage(<CustomersPage />, '/customers?page=999')
  expect(await screen.findByText('No customers on this page')).toBeVisible()
  await userEvent.click(
    screen.getByRole('button', { name: 'Go to first page' }),
  )
  expect(await screen.findByText(customer.name)).toBeVisible()
  expect(screen.getByTestId('location')).toHaveTextContent('page=1')
})
