import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { AppLayout } from './AppLayout'
import { renderPage } from '@/test/render'
import { mockApi, json } from '@/test/http'

afterEach(() => vi.unstubAllGlobals())
function workspace() {
  mockApi(() => json({ status: 'Healthy' }))
  return renderPage(
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<h1>Overview test page</h1>} />
        <Route path="shipments" element={<h1>Shipments test page</h1>} />
      </Route>
    </Routes>,
  )
}
it('supports search keyboard shortcuts and preserves a usable collapsed navigation', async () => {
  workspace()
  await userEvent.click(
    screen.getByRole('button', { name: 'Collapse sidebar' }),
  )
  expect(
    screen.getByRole('button', { name: 'Expand sidebar' }),
  ).toHaveAttribute('aria-expanded', 'false')
  expect(screen.getByRole('link', { name: 'Shipments' })).toHaveAttribute(
    'href',
    '/shipments',
  )
  await userEvent.keyboard('{Control>}k{/Control}')
  const search = screen.getByLabelText('Search all shipments')
  expect(search).toHaveFocus()
  await userEvent.type(search, 'Vienna{Enter}')
  expect(screen.getByTestId('location')).toHaveTextContent(
    '/shipments?search=Vienna',
  )
  expect(document.title).toBe('FleetFlow · Shipments')
})
it('moves desktop keyboard focus into the new page when following sidebar navigation', async () => {
  workspace()
  const link = screen.getByRole('link', { name: 'Shipments' })
  link.focus()
  await userEvent.keyboard('{Enter}')
  expect(
    screen.getByRole('heading', { name: 'Shipments test page' }),
  ).toBeVisible()
  expect(screen.getByRole('main')).toHaveFocus()
  expect(screen.getByRole('link', { name: 'Shipments' })).toHaveAttribute(
    'aria-current',
    'page',
  )
})
