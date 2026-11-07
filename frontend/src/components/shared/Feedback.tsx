import { AlertCircle, Inbox, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { ApiError } from '@/lib/api'
export function ErrorNotice({
  error,
  retry,
}: {
  error: unknown
  retry?: () => void
}) {
  const apiError = error instanceof ApiError ? error : undefined
  return (
    <div role="alert" className="error-notice">
      <AlertCircle size={19} className="shrink-0 mt-0.5" />
      <div className="min-w-0">
        <p className="font-semibold">
          {apiError?.status === 404
            ? 'This record could not be found'
            : 'Something needs your attention'}
        </p>
        <p className="mt-1 text-sm">
          {error instanceof Error
            ? error.message
            : 'The request could not be completed.'}
        </p>
        {apiError?.traceId && (
          <p className="mt-2 text-xs opacity-70 break-all">
            Reference: {apiError.traceId}
          </p>
        )}
        {retry && (
          <Button className="mt-3" variant="outline" size="sm" onClick={retry}>
            <RefreshCw size={14} />
            Try again
          </Button>
        )}
      </div>
    </div>
  )
}
export function EmptyState({
  title,
  description,
  action,
}: {
  title: string
  description: string
  action?: React.ReactNode
}) {
  return (
    <div className="empty-state">
      <span className="empty-icon">
        <Inbox size={25} />
      </span>
      <h3 className="mt-4 font-semibold">{title}</h3>
      <p className="mt-2 max-w-md text-sm text-slate-500">{description}</p>
      {action && <div className="mt-5">{action}</div>}
    </div>
  )
}
export function Skeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div aria-label="Loading data" role="status" className="p-6 space-y-5">
      <span className="sr-only">Loading data…</span>
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="skeleton h-9" />
      ))}
    </div>
  )
}
export function StaleNotice({
  onReload,
  busy,
}: {
  onReload: () => void
  busy?: boolean
}) {
  return (
    <div role="alert" className="error-notice">
      <AlertCircle size={20} className="shrink-0" />
      <div>
        <p className="font-semibold">Shipment changed elsewhere</p>
        <p className="mt-1 text-sm">
          Your changes have not been saved. Reload the latest shipment before
          reviewing and submitting again.
        </p>
        <Button
          variant="outline"
          size="sm"
          className="mt-3"
          onClick={onReload}
          disabled={busy}
        >
          Discard edits and reload latest
        </Button>
      </div>
    </div>
  )
}
