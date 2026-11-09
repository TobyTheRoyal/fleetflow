import { useMutation, useQuery } from '@tanstack/react-query'
import { Navigate, useNavigate } from 'react-router-dom'
import { Eye, Truck, Route, ShieldCheck, RefreshCw, Clock3 } from 'lucide-react'
import { request } from '@/lib/api'
import { sessionSchema, useSession } from '@/features/auth/context'
import { Button } from '@/components/ui/button'
import { ErrorNotice, Skeleton } from '@/components/shared/Feedback'
import { demoAvailabilitySchema } from './contracts'
export function DemoPage() {
  const session = useSession()
  const navigate = useNavigate()
  const availability = useQuery({
    queryKey: ['demo', 'availability'],
    enabled: session.portfolioMode === true,
    retry: false,
    queryFn: ({ signal }) =>
      request('/api/v1/demo', demoAvailabilitySchema, { signal }),
  })
  const start = useMutation({
    retry: false,
    mutationFn: (mode: 'Viewer' | 'Dispatcher') =>
      request('/api/v1/demo/sessions', sessionSchema, {
        method: 'POST',
        body: { mode },
      }),
    onSuccess: (user) => {
      session.signedIn(user)
      navigate('/')
    },
    onError: () => {
      void availability.refetch()
    },
  })
  if (!session.portfolioMode) return <Navigate to="/login" replace />
  if (session.user) return <Navigate to="/" replace />
  return (
    <main className="min-h-screen bg-[#f5f7fb] px-5 py-10 sm:py-16">
      <div className="mx-auto max-w-5xl">
        <div className="flex items-center gap-3 text-blue-700">
          <Route size={32} />
          <span className="text-2xl font-bold tracking-tight text-slate-900">
            FleetFlow
          </span>
        </div>
        <p className="eyebrow mt-10">PUBLIC PORTFOLIO DEMO</p>
        <h1 className="mt-3 text-3xl sm:text-4xl font-semibold tracking-tight text-slate-900">
          A working logistics workspace.
        </h1>
        <p className="mt-4 max-w-2xl text-slate-600 leading-7">
          Explore real dispatch workflows, operational insights and live
          collaboration. No registration or password is required. All logistics
          records are fictional.
        </p>
        {session.demoExpired && (
          <p
            role="status"
            className="mt-6 rounded-xl border border-blue-200 bg-blue-50 p-4 text-blue-950"
          >
            Your temporary demo session has ended. Local edits were discarded to
            protect visitor isolation. Start a new session to continue with a
            fresh workspace.
          </p>
        )}
        {availability.isPending && (
          <div className="mt-6">
            <Skeleton rows={2} />
          </div>
        )}
        {availability.isError && (
          <div className="mt-6">
            <ErrorNotice
              error={availability.error}
              retry={() => void availability.refetch()}
            />
          </div>
        )}
        {start.isError && (
          <div className="mt-6">
            <ErrorNotice error={start.error} />
          </div>
        )}
        <div className="mt-8 grid gap-5 md:grid-cols-2">
          {(
            [
              {
                mode: 'Viewer',
                title: 'Explore Read-Only Demo',
                icon: Eye,
                description:
                  'Browse the operations dashboard, shipments, fleet and verified operational findings. Records stay protected from changes.',
                count: availability.data?.viewerAvailable,
              },
              {
                mode: 'Dispatcher',
                title: 'Start Interactive Demo',
                icon: Truck,
                description:
                  'Create, assign and deliver fictional shipments in your own isolated sandbox. Open a second tab to experience live updates.',
                count: availability.data?.dispatcherAvailable,
              },
            ] as const
          ).map(({ mode, title, icon: Icon, description, count }) => (
            <section key={mode} className="panel p-6 sm:p-8 flex flex-col">
              <Icon size={27} className="text-blue-700" />
              <h2 className="mt-5 text-xl font-semibold">
                {mode === 'Viewer' ? 'Look around' : 'Take the dispatch desk'}
              </h2>
              <p className="mt-3 flex-1 text-sm text-slate-600 leading-7">
                {description}
              </p>
              <p className="mt-5 text-sm text-slate-600" role="status">
                {count === undefined
                  ? 'Checking availability…'
                  : `${count} ${mode === 'Viewer' ? 'read-only places' : 'interactive sandboxes'} available`}
              </p>
              <Button
                className="mt-5 w-full"
                disabled={
                  !availability.data ||
                  count === 0 ||
                  start.isPending ||
                  !session.ready
                }
                onClick={() => start.mutate(mode)}
              >
                {start.isPending && start.variables === mode
                  ? 'Preparing your workspace…'
                  : title}
              </Button>
            </section>
          ))}
        </div>
        <div className="mt-6 panel p-6 text-sm leading-7 text-slate-600">
          <p className="flex items-start gap-3">
            <Clock3 size={18} className="mt-1 shrink-0" />
            Sessions last up to {availability.data?.leaseMinutes ?? 30} minutes.
            Unsaved work is discarded when your session ends; interactive
            sandboxes are reset before reuse.
          </p>
          <p className="mt-3 flex items-start gap-3">
            <ShieldCheck size={18} className="mt-1 shrink-0" />
            Use fictional information only. Administrator access is unavailable.
            Cloud AI requires explicit account approval and shared daily free
            quotas; deterministic insights work without it.
          </p>
        </div>
        <Button
          className="mt-5"
          variant="outline"
          disabled={availability.isFetching || start.isPending}
          onClick={() => void availability.refetch()}
        >
          <RefreshCw size={16} />
          Refresh availability
        </Button>
        <p className="mt-8 text-xs text-slate-600">
          Independent C# / ASP.NET Core, React and PostgreSQL portfolio project.
          Free hosting may sleep or reach its usage limits.
        </p>
      </div>
    </main>
  )
}
