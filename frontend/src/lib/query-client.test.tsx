import { afterEach, expect, it, vi } from 'vitest'
import { createQueryClient } from './query-client'
import { ApiError } from './api'
import { renderHook, act } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useShipmentWrite } from './queries'
import { shipment } from '@/test/fixtures'
import { json, mockApi } from '@/test/http'
afterEach(() => vi.unstubAllGlobals())
it('retries a transient read once but never a validation/conflict response', async () => {
  const client = createQueryClient()
  const read = vi
    .fn()
    .mockRejectedValueOnce(
      new ApiError('Offline', 503, 'dependency_unavailable'),
    )
    .mockResolvedValue('ready')
  expect(
    await client.fetchQuery({
      queryKey: ['transient'],
      queryFn: read,
      retryDelay: 0,
    }),
  ).toBe('ready')
  expect(read).toHaveBeenCalledTimes(2)
  for (const status of [400, 404, 409, 422]) {
    const fail = vi
      .fn()
      .mockRejectedValue(new ApiError('Rejected', status, 'business_error'))
    await expect(
      client.fetchQuery({ queryKey: [status], queryFn: fail, retryDelay: 0 }),
    ).rejects.toBeInstanceOf(ApiError)
    expect(fail).toHaveBeenCalledTimes(1)
  }
  client.clear()
})
it('does not repeat writes and invalidates detail/history/lists/dashboard after success', async () => {
  const client = createQueryClient()
  const invalidate = vi.spyOn(client, 'invalidateQueries')
  const fetch = mockApi(() => json({ ...shipment, version: 2 }))
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
  const { result } = renderHook(() => useShipmentWrite(shipment.id), {
    wrapper,
  })
  await act(async () => {
    await result.current.mutateAsync({
      action: 'status',
      body: { version: 1, status: 'Cancelled', reason: null },
    })
  })
  expect(invalidate).toHaveBeenCalledWith({ queryKey: ['shipments'] })
  expect(invalidate).toHaveBeenCalledWith({ queryKey: ['dashboard'] })
  fetch.mockImplementation(() =>
    Promise.resolve(
      json({ code: 'stale_version', detail: 'Changed elsewhere.' }, 409),
    ),
  )
  await act(async () => {
    await expect(
      result.current.mutateAsync({
        action: 'status',
        body: { version: 1, status: 'Cancelled', reason: null },
      }),
    ).rejects.toMatchObject({ code: 'stale_version' })
  })
  expect(
    fetch.mock.calls.filter(([, options]) => options.method === 'POST'),
  ).toHaveLength(2)
  client.clear()
})
