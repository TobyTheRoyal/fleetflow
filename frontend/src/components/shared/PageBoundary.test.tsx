import { expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes, useNavigate } from 'react-router-dom'
import { PageBoundary } from './PageBoundary'
import { renderPage } from '@/test/render'
function Broken(): never {
  throw new Error('Private implementation detail')
}
function Navigation() {
  const navigate = useNavigate()
  return (
    <button onClick={() => navigate('/healthy')}>
      Navigate to healthy page
    </button>
  )
}
it('shows a safe recovery boundary and resets after navigation', async () => {
  vi.spyOn(console, 'error').mockImplementation(() => {})
  renderPage(
    <>
      <Navigation />
      <Routes>
        <Route element={<PageBoundary />}>
          <Route path="/broken" element={<Broken />} />
          <Route path="/healthy" element={<p>Recovered view</p>} />
        </Route>
      </Routes>
    </>,
    '/broken',
  )
  expect(screen.getByRole('alert')).toHaveTextContent(
    'This page could not be displayed',
  )
  expect(
    screen.queryByText('Private implementation detail'),
  ).not.toBeInTheDocument()
  expect(
    screen.getByRole('link', { name: 'Return to overview' }),
  ).toHaveAttribute('href', '/')
  await userEvent.click(
    screen.getByRole('button', { name: 'Navigate to healthy page' }),
  )
  expect(screen.getByText('Recovered view')).toBeVisible()
})
