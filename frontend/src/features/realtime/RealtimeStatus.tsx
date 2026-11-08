import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { verifyCurrentUser } from '@/features/auth/verify-session'
import { useSession } from '@/features/auth/context'
import {
  createConnection,
  manageConnection,
  type LiveStatus,
} from './connection'
export function RealtimeStatus() {
  const { user, locked, ready } = useSession()
  const userId = user?.id
  const cache = useQueryClient()
  const [status, setStatus] = useState<LiveStatus>('Disconnected')
  useEffect(() => {
    if (!ready || !userId || locked) return
    return manageConnection(cache, setStatus, createConnection, (signal) =>
      verifyCurrentUser(userId, signal),
    )
  }, [cache, ready, userId, locked])
  const display = !user || locked ? 'Disconnected' : status
  return (
    <span
      className={`realtime-status ${display === 'Connected' ? 'live' : ''}`}
      role="status"
      aria-label={`Real-time: ${display}`}
      title={
        display === 'Connected'
          ? 'Live logistics updates are connected.'
          : 'Live updates are unavailable. Data refreshes every 30 seconds; you can continue using FleetFlow.'
      }
    >
      <span aria-hidden="true" className="realtime-dot" />
      <span>{display}</span>
    </span>
  )
}
