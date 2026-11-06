import { useQuery } from '@tanstack/react-query'
import { ArrowUpRight, RefreshCw, Truck } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { getReadiness } from '@/lib/health'

export function App() {
  const health = useQuery({
    queryKey: ['health', 'ready'],
    queryFn: ({ signal }) => getReadiness(signal),
  })

  return (
    <div className="min-h-screen bg-slate-50 text-slate-950">
      <header className="border-b bg-white">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 px-6 py-5">
          <a
            href="/"
            className="flex items-center gap-3 rounded-sm font-semibold tracking-tight focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-slate-900"
            aria-label="FleetFlow home"
          >
            <span className="rounded-lg bg-slate-900 p-2 text-white">
              <Truck size={22} aria-hidden="true" />
            </span>
            FleetFlow
          </a>
          <span className="rounded-full border px-3 py-1 text-xs font-medium text-slate-600">
            Local development · M0
          </span>
        </div>
      </header>
      <main className="mx-auto max-w-5xl px-6 py-16 sm:py-24">
        <p className="text-sm font-semibold uppercase tracking-widest text-slate-500">
          Logistics & fleet management
        </p>
        <h1 className="mt-4 max-w-2xl text-4xl font-semibold leading-tight tracking-tight sm:text-5xl">
          A solid foundation for the road ahead.
        </h1>
        <p className="mt-6 max-w-xl text-base leading-7 text-slate-600">
          FleetFlow is an independent portfolio project. This workspace
          establishes the application infrastructure. Shipment and fleet
          workflows will follow in later milestones.
        </p>

        <section
          aria-labelledby="connection-title"
          className="mt-10 max-w-xl rounded-xl border bg-white p-6 shadow-sm"
        >
          <h2 id="connection-title" className="font-semibold">
            Application connection
          </h2>
          <div
            role="status"
            aria-live="polite"
            aria-atomic="true"
            className="mt-3 text-sm leading-6"
          >
            {health.isPending ? (
              <p className="text-slate-600">
                Checking API and database connection…
              </p>
            ) : health.isError ? (
              <p className="text-red-800">
                {health.error instanceof Error
                  ? health.error.message
                  : 'Connection unavailable.'}
              </p>
            ) : (
              <p className="text-emerald-800">
                API and PostgreSQL are connected.
              </p>
            )}
          </div>
          <div className="mt-5 flex flex-wrap gap-3">
            <Button
              variant="outline"
              size="sm"
              disabled={health.isFetching}
              onClick={() => void health.refetch()}
            >
              <RefreshCw
                size={15}
                aria-hidden="true"
                className={health.isFetching ? 'animate-spin' : undefined}
              />
              {health.isFetching ? 'Checking…' : 'Check connection'}
            </Button>
            <Button variant="outline" size="sm" asChild>
              <a href="/openapi/v1.json" target="_blank" rel="noreferrer">
                OpenAPI JSON <ArrowUpRight size={15} aria-hidden="true" />
                <span className="sr-only"> (opens in a new tab)</span>
              </a>
            </Button>
          </div>
        </section>
        <p className="mt-8 text-xs text-slate-500">
          Development environment only. Authentication and public deployment are
          outside M0.
        </p>
      </main>
    </div>
  )
}
