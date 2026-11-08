import { afterEach, expect, it, vi } from 'vitest'
import { QueryClient } from '@tanstack/react-query'
import { manageConnection, type LiveConnection } from './connection'
function connection() {
  const callbacks: {
    close?: (error?: Error) => void
    reconnecting?: (error?: Error) => void
    reconnected?: (id?: string) => void
  } = {}
  const live: LiveConnection = {
    start: vi.fn(async () => {}),
    stop: vi.fn(async () => {}),
    invoke: vi.fn(async () => undefined),
    on: vi.fn(),
    off: vi.fn(),
    onclose: (cb) => {
      callbacks.close = cb
    },
    onreconnecting: (cb) => {
      callbacks.reconnecting = cb
    },
    onreconnected: (cb) => {
      callbacks.reconnected = cb
    },
  }
  return { live, callbacks }
}
afterEach(() => vi.useRealTimers())
it('connects once and recovers missed messages after reconnect', async () => {
  const { live, callbacks } = connection()
  const cache = new QueryClient()
  const invalidate = vi.spyOn(cache, 'invalidateQueries')
  const status = vi.fn()
  const session = vi.fn(async () => undefined)
  const stop = manageConnection(cache, status, async () => live, session)
  await vi.waitFor(() => expect(status).toHaveBeenLastCalledWith('Connected'))
  expect(live.invoke).toHaveBeenCalledWith('Ready')
  expect(invalidate).toHaveBeenCalledTimes(5)
  callbacks.reconnecting?.()
  expect(status).toHaveBeenLastCalledWith('Reconnecting')
  expect(session).toHaveBeenCalledTimes(2)
  callbacks.reconnected?.()
  await vi.waitFor(() => expect(invalidate).toHaveBeenCalledTimes(10))
  stop()
  expect(live.off).toHaveBeenCalledWith(
    'LogisticsChanged',
    expect.any(Function),
  )
  expect(live.stop).toHaveBeenCalledOnce()
})
it('backs off failed starts, polls data and stops retries on logout', async () => {
  vi.useFakeTimers()
  const cache = new QueryClient()
  const invalidate = vi.spyOn(cache, 'invalidateQueries')
  const status = vi.fn()
  const factory = vi.fn(async () => {
    throw new Error('Offline')
  })
  const session = vi.fn(async () => undefined)
  const stop = manageConnection(cache, status, factory, session)
  await vi.advanceTimersByTimeAsync(0)
  expect(status).toHaveBeenLastCalledWith('Disconnected')
  expect(factory).toHaveBeenCalledOnce()
  await vi.advanceTimersByTimeAsync(30_000)
  expect(invalidate).toHaveBeenCalledTimes(5)
  expect(factory).toHaveBeenCalledTimes(4)
  stop()
  await vi.advanceTimersByTimeAsync(120_000)
  expect(factory).toHaveBeenCalledTimes(4)
  expect(invalidate).toHaveBeenCalledTimes(5)
})
it('cleans up if logout happens during connection startup', async () => {
  const { live } = connection()
  let complete: () => void = () => {}
  vi.mocked(live.start).mockImplementation(
    () =>
      new Promise<void>((resolve) => {
        complete = resolve
      }),
  )
  const status = vi.fn()
  const cache = new QueryClient()
  const invalidate = vi.spyOn(cache, 'invalidateQueries')
  const stop = manageConnection(
    cache,
    status,
    async () => live,
    async () => undefined,
  )
  await vi.waitFor(() => expect(live.start).toHaveBeenCalledOnce())
  stop()
  complete()
  await vi.waitFor(() => expect(live.stop).toHaveBeenCalled())
  expect(invalidate).not.toHaveBeenCalled()
  expect(status).not.toHaveBeenCalledWith('Connected')
})
it('rechecks the session on closure and reconnects with fresh CSRF configuration', async () => {
  vi.useFakeTimers()
  const first = connection()
  const second = connection()
  const factory = vi
    .fn()
    .mockResolvedValueOnce(first.live)
    .mockResolvedValue(second.live)
  const session = vi.fn(async () => undefined)
  const stop = manageConnection(new QueryClient(), vi.fn(), factory, session)
  await vi.advanceTimersByTimeAsync(0)
  first.callbacks.close?.()
  expect(session).toHaveBeenCalledTimes(2)
  await vi.advanceTimersByTimeAsync(2000)
  expect(factory).toHaveBeenCalledTimes(2)
  expect(second.live.start).toHaveBeenCalledOnce()
  stop()
})
it('periodically reconciles even a healthy connection to recover a silently lost hint', async () => {
  vi.useFakeTimers()
  const { live } = connection()
  const cache = new QueryClient()
  const invalidate = vi.spyOn(cache, 'invalidateQueries')
  const stop = manageConnection(
    cache,
    vi.fn(),
    async () => live,
    async () => undefined,
  )
  await vi.advanceTimersByTimeAsync(0)
  expect(invalidate).toHaveBeenCalledTimes(5)
  await vi.advanceTimersByTimeAsync(30_000)
  expect(invalidate).toHaveBeenCalledTimes(5)
  await vi.advanceTimersByTimeAsync(30_000)
  expect(invalidate).toHaveBeenCalledTimes(10)
  stop()
})
it('aborts pending session verification when the workspace is disposed', async () => {
  const { live } = connection()
  const verify = vi.fn(
    (signal?: AbortSignal) =>
      new Promise<void>((_resolve, reject) => {
        signal?.addEventListener('abort', () =>
          reject(new DOMException('Aborted', 'AbortError')),
        )
      }),
  )
  const status = vi.fn()
  const stop = manageConnection(
    new QueryClient(),
    status,
    async () => live,
    verify,
  )
  await vi.waitFor(() => expect(verify).toHaveBeenCalledOnce())
  const signal = verify.mock.calls[0]?.[0]
  stop()
  expect(signal?.aborted).toBe(true)
  expect(live.invoke).not.toHaveBeenCalled()
  expect(status).not.toHaveBeenCalledWith('Connected')
})
