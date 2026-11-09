import { useEffect, useState, Fragment, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Navigate, Outlet } from 'react-router-dom'
import { z } from 'zod'
import {
  SessionContext,
  useSession,
  useCanManage,
  sessionSchema,
  type SessionUser,
} from './context'
import { ApiError, request, resetCsrf } from '@/lib/api'
import { Dialog } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { ErrorNotice, Skeleton } from '@/components/shared/Feedback'
import { LoginForm } from './LoginForm'
export function SessionProvider({
  children,
  portfolioMode = false,
}: {
  children: ReactNode
  portfolioMode?: boolean
}) {
  const cache = useQueryClient()
  const [override, setUser] = useState<SessionUser | null | undefined>(
    undefined,
  )
  const restored = useQuery({
    queryKey: ['session'],
    queryFn: ({ signal }) =>
      request('/api/v1/auth/me', sessionSchema, { signal }),
    retry: false,
  })
  const user = override === undefined ? (restored.data ?? null) : override
  const ready = override !== undefined || !restored.isPending
  const [locked, setLocked] = useState(false)
  const [demoExpired, setDemoExpired] = useState(false)
  const [error, setError] = useState<unknown>(null)
  useEffect(() => {
    const expire = () => {
      if (user?.demo) {
        resetCsrf()
        cache.clear()
        setUser(null)
        setLocked(false)
        setDemoExpired(true)
      } else setLocked(true)
    }
    window.addEventListener('fleetflow:unauthorized', expire)
    return () => window.removeEventListener('fleetflow:unauthorized', expire)
    // Restoration runs once; mutations control subsequent session changes.
  }, [user?.demo, cache])
  useEffect(() => {
    if (!user?.demo) return
    const timer = setTimeout(
      () => window.dispatchEvent(new Event('fleetflow:unauthorized')),
      Math.max(0, Date.parse(user.demo.expiresAtUtc) - Date.now()),
    )
    return () => clearTimeout(timer)
  }, [user?.demo])
  const signedIn = (next: SessionUser) => {
    resetCsrf()
    if (user?.id !== next.id || user?.demo?.leaseId !== next.demo?.leaseId)
      cache.clear()
    else void cache.invalidateQueries()
    setUser(next)
    setLocked(false)
    setDemoExpired(false)
  }
  const logout = async () => {
    try {
      await request('/api/v1/auth/logout', z.object({ success: z.boolean() }), {
        method: 'POST',
      })
    } catch (failure) {
      if (!(failure instanceof ApiError && failure.status === 401)) {
        setError(failure)
        throw failure
      }
    }
    resetCsrf()
    cache.clear()
    setUser(null)
    setLocked(false)
    setError(null)
    setDemoExpired(false)
  }
  const restorationError =
    restored.error instanceof ApiError && restored.error.status === 401
      ? null
      : restored.error
  if ((error || restorationError) && !user)
    return (
      <div className="login-page">
        <ErrorNotice
          error={error ?? restorationError}
          retry={() => void restored.refetch()}
        />
      </div>
    )
  return (
    <SessionContext.Provider
      value={{
        user,
        ready,
        locked,
        signedIn,
        logout,
        portfolioMode,
        demoExpired,
      }}
    >
      <Fragment
        key={user ? `${user.id}:${user.demo?.leaseId ?? ''}` : 'anonymous'}
      >
        {children}
      </Fragment>
      {locked && user && (
        <Dialog
          open
          onOpenChange={() => {}}
          title="Session expired"
          description="Sign in again to continue. Unsaved form data stays in this tab for the same account."
        >
          <div className="dialog-body">
            <LoginForm reauthenticate />
            {error ? <ErrorNotice error={error} /> : null}
            <Button
              variant="outline"
              className="mt-4"
              onClick={() => {
                void logout().catch(() => {})
              }}
            >
              Sign out and discard local edits
            </Button>
          </div>
        </Dialog>
      )}
    </SessionContext.Provider>
  )
}
export function ProtectedRoute({
  write = false,
  admin = false,
}: {
  write?: boolean
  admin?: boolean
}) {
  const session = useSession()
  const manage = useCanManage()
  if (!session.ready) return <Skeleton />
  if (!session.user)
    return <Navigate to={session.portfolioMode ? '/demo' : '/login'} replace />
  if ((write && !manage) || (admin && session.user.role !== 'Admin'))
    return (
      <div className="p-8">
        <ErrorNotice
          error={
            new ApiError(
              'Your role does not permit this page.',
              403,
              'forbidden',
            )
          }
        />
      </div>
    )
  return <Outlet />
}
