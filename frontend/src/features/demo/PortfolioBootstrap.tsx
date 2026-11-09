import { useEffect, useState, type ReactNode } from 'react'
import { request, ApiError } from '@/lib/api'
import { ErrorNotice, Skeleton } from '@/components/shared/Feedback'
import { demoAvailabilitySchema } from './contracts'
export function PortfolioBootstrap({
  children,
}: {
  children: (enabled: boolean) => ReactNode
}) {
  const [mode, setMode] = useState<boolean>()
  const [error, setError] = useState<unknown>()
  const [attempt, setAttempt] = useState(0)
  useEffect(() => {
    const abort = new AbortController()
    void request('/api/v1/demo', demoAvailabilitySchema, {
      signal: abort.signal,
    })
      .then((config) => setMode(config.enabled))
      .catch((failure: unknown) => {
        if (abort.signal.aborted) return
        if (failure instanceof ApiError && failure.status === 404)
          setMode(false)
        else setError(failure)
      })
    return () => abort.abort()
  }, [attempt])
  if (mode !== undefined) return children(mode)
  return (
    <main className="login-page">
      <section className="login-card">
        <h1 className="text-2xl font-semibold mb-5">FleetFlow</h1>
        {error ? (
          <ErrorNotice
            error={error}
            retry={() => {
              setError(undefined)
              setAttempt((n) => n + 1)
            }}
          />
        ) : (
          <>
            <p className="mb-4 text-slate-600">
              Connecting to FleetFlow. A sleeping demo service may take a moment
              to start.
            </p>
            <Skeleton rows={2} />
          </>
        )}
      </section>
    </main>
  )
}
