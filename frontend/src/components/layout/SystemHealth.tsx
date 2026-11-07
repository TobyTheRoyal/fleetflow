import { useQuery } from '@tanstack/react-query'
import { ChevronDown, RefreshCw } from 'lucide-react'
import { getReadiness } from '@/lib/health'
import { Button } from '@/components/ui/button'
export function SystemHealth() {
  const health = useQuery({
    queryKey: ['health', 'ready'],
    queryFn: ({ signal }) => getReadiness(signal),
    refetchInterval: 60000,
  })
  const message = health.isPending
    ? 'Checking API and database connection…'
    : health.isError
      ? health.error.message
      : 'API and PostgreSQL are connected.'
  return (
    <div className="relative">
      <span
        data-testid="connection-status"
        role="status"
        aria-live="polite"
        className="sr-only"
      >
        {message}
      </span>
      <details className="health-details">
        <summary>
          <span
            className={`health-dot ${health.isError ? 'bad' : health.isPending ? 'checking' : ''}`}
          />
          <span>
            {health.isError
              ? 'Connection issue'
              : health.isPending
                ? 'Connecting'
                : 'Systems operational'}
          </span>
          <ChevronDown size={12} />
        </summary>
        <div className="health-popover">
          <h2 className="font-semibold text-sm">Application connection</h2>
          <p className="mt-2 text-sm text-slate-500">{message}</p>
          <Button
            variant="outline"
            size="sm"
            className="mt-4"
            onClick={() => void health.refetch()}
            disabled={health.isFetching}
          >
            <RefreshCw
              size={14}
              className={health.isFetching ? 'animate-spin' : ''}
            />
            {health.isFetching ? 'Checking…' : 'Check connection'}
          </Button>
        </div>
      </details>
    </div>
  )
}
