import { Component, Suspense, type ReactNode } from 'react'
import { useLocation, Outlet } from 'react-router-dom'
import { Skeleton } from './Feedback'
class PageErrorBoundary extends Component<
  { children: ReactNode },
  { failed: boolean }
> {
  state = { failed: false }
  static getDerivedStateFromError() {
    return { failed: true }
  }
  render() {
    return this.state.failed ? (
      <div className="panel p-8" role="alert">
        <h1>This page could not be displayed</h1>
        <p className="mt-4 text-sm text-slate-500">
          Reload FleetFlow to try again. Your saved data is held by the server.
        </p>
        <a className="text-button mt-5" href="/">
          Return to overview
        </a>
      </div>
    ) : (
      this.props.children
    )
  }
}
export function PageBoundary() {
  const location = useLocation()
  return (
    <PageErrorBoundary key={location.pathname}>
      <Suspense fallback={<Skeleton rows={6} />}>
        <Outlet />
      </Suspense>
    </PageErrorBoundary>
  )
}
