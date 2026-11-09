import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useSession } from '@/features/auth/context'
import { Button } from '@/components/ui/button'
import { Dialog } from '@/components/ui/dialog'
import { ErrorNotice } from '@/components/shared/Feedback'
export function DemoBanner() {
  const { user, logout } = useSession()
  const [now, setNow] = useState(() => Date.now())
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  const navigate = useNavigate()
  const expires = user?.demo?.expiresAtUtc
  useEffect(() => {
    if (!expires) return
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [expires])
  if (!user?.demo) return null
  const remaining = Math.max(
    0,
    Math.ceil((Date.parse(user.demo.expiresAtUtc) - now) / 1000),
  )
  return (
    <>
      <aside
        className="mb-5 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-blue-200 bg-blue-50 p-4 text-sm text-blue-950"
        aria-label="Temporary demo session"
      >
        <div>
          <strong>
            {user.role === 'Viewer'
              ? 'Read-only demo'
              : 'Isolated dispatcher sandbox'}
          </strong>
          <p className="mt-1">
            Fictional data only ·{' '}
            <span aria-label="Remaining session time">
              {Math.floor(remaining / 60)}:
              {String(remaining % 60).padStart(2, '0')}
            </span>{' '}
            remaining · Changes are temporary.
          </p>
        </div>
        <Button variant="outline" onClick={() => setConfirm(true)}>
          End demo
        </Button>
      </aside>
      <Dialog
        open={confirm}
        onOpenChange={setConfirm}
        title="End this demo?"
        description="Your session will be revoked. Local edits will be discarded and this sandbox will be reset before reuse."
      >
        <div className="dialog-body">
          {error ? <ErrorNotice error={error} /> : null}
          <Button
            disabled={busy}
            onClick={async () => {
              setBusy(true)
              try {
                await logout()
                navigate('/demo')
              } catch (failure) {
                setError(failure)
              } finally {
                setBusy(false)
              }
            }}
          >
            {busy ? 'Ending demo…' : 'End demo and discard edits'}
          </Button>
        </div>
      </Dialog>
    </>
  )
}
