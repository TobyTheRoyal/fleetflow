import {
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr'
import type { QueryClient } from '@tanstack/react-query'
import { apiBase, csrfToken, request } from '@/lib/api'
import { sessionSchema } from '@/features/auth/context'
import { notificationHandler, refreshLogistics } from './notifications'
export type LiveStatus = 'Connected' | 'Reconnecting' | 'Disconnected'
export type LiveConnection = Pick<
  HubConnection,
  | 'start'
  | 'stop'
  | 'on'
  | 'off'
  | 'onclose'
  | 'onreconnecting'
  | 'onreconnected'
> & { invoke(methodName: string): Promise<void> }
export async function createConnection(): Promise<LiveConnection> {
  const token = await csrfToken()
  return (
    new HubConnectionBuilder()
      .withUrl(apiBase + '/hubs/logistics', {
        withCredentials: true,
        headers: { 'X-CSRF-TOKEN': token },
      })
      .withAutomaticReconnect([0, 2000, 10000, 30000])
      // Connection URLs include transport tokens. Do not write them to browser logs.
      .configureLogging(LogLevel.None)
      .build()
  )
}
export function manageConnection(
  cache: QueryClient,
  status: (value: LiveStatus) => void,
  factory = createConnection,
  verifySession: (signal?: AbortSignal) => Promise<unknown> = (signal) =>
    request('/api/v1/auth/me', sessionSchema, { signal }),
) {
  const pending = new AbortController()
  let disposed = false
  let connection: LiveConnection | undefined
  let retry: ReturnType<typeof setTimeout> | undefined
  let failures = 0
  let connected = false
  const notification = notificationHandler(cache)
  const verify = () => void verifySession(pending.signal).catch(() => {})
  const schedule = () => {
    if (disposed || retry) return
    const delays = [2000, 5000, 10000, 30000]
    retry = setTimeout(
      () => {
        retry = undefined
        void start()
      },
      delays[Math.min(failures++, delays.length - 1)],
    )
  }
  const ready = async (current: LiveConnection) => {
    await verifySession(pending.signal)
    await current.invoke('Ready')
    if (disposed || connection !== current) return
    connected = true
    failures = 0
    status('Connected')
    // Initial connect and every reconnect recover missed hints through REST.
    await refreshLogistics(cache)
  }
  const start = async () => {
    if (disposed) return
    status('Reconnecting')
    try {
      const current = await factory()
      if (disposed) {
        await current.stop()
        return
      }
      connection = current
      current.on('LogisticsChanged', notification)
      current.onreconnecting(() => {
        if (disposed) return
        connected = false
        status('Reconnecting')
        verify()
      })
      current.onreconnected(() => {
        if (disposed) return
        void ready(current).catch(() => current.stop())
      })
      current.onclose(() => {
        current.off('LogisticsChanged', notification)
        if (disposed || current !== connection) return
        connected = false
        status('Disconnected')
        verify()
        schedule()
      })
      await current.start()
      if (disposed) {
        await current.stop()
        return
      }
      await ready(current)
    } catch {
      if (disposed) return
      connected = false
      const failed = connection
      connection = undefined
      failed?.off('LogisticsChanged', notification)
      await failed?.stop().catch(() => {})
      status('Disconnected')
      verify()
      schedule()
    }
  }
  // A usable fallback without full-page reloads. Active queries only are fetched.
  let reconciliationTick = 0
  const fallback = setInterval(() => {
    reconciliationTick++
    // Also reconcile healthy sockets every minute: commit-to-send is best effort.
    if (!disposed && (!connected || reconciliationTick % 2 === 0)) {
      verify()
      void refreshLogistics(cache)
    }
  }, 30_000)
  void start()
  return () => {
    disposed = true
    pending.abort()
    clearInterval(fallback)
    clearTimeout(retry)
    connection?.off('LogisticsChanged', notification)
    void connection?.stop().catch(() => {})
  }
}
