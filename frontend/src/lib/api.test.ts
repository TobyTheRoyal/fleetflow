import { afterEach, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { request, ApiError } from './api'
import { json, mockApi } from '@/test/http'
afterEach(() => vi.unstubAllGlobals())
it.each([400, 404, 409, 422, 503])(
  'preserves safe ProblemDetails for HTTP %s',
  async (status) => {
    mockApi(() =>
      json(
        {
          code: 'test_code',
          detail: 'Safe explanation',
          traceId: 'trace123',
          errors: { WeightKg: ['Invalid weight'] },
        },
        status,
      ),
    )
    await expect(request('/api/v1/test', z.object({}))).rejects.toMatchObject({
      status,
      code: 'test_code',
      message: 'Safe explanation',
      traceId: 'trace123',
      fields: { WeightKg: ['Invalid weight'] },
    })
  },
)
it('rejects incompatible success payloads', async () => {
  mockApi(() => json({ value: 'bad' }))
  await expect(
    request('/test', z.object({ value: z.number() })),
  ).rejects.toMatchObject({ code: 'invalid_response' })
})
it('passes request cancellation through without hiding it as a network error', async () => {
  const controller = new AbortController()
  const error = new DOMException('Aborted', 'AbortError')
  const fetch = mockApi(() => Promise.reject(error))
  controller.abort()
  await expect(
    request('/test', z.object({}), { signal: controller.signal }),
  ).rejects.toBe(error)
  expect(fetch.mock.calls[0]![1]!.signal).toBe(controller.signal)
})
it('provides a useful network failure without raw transport details', async () => {
  mockApi(() => Promise.reject(new Error('private connection detail')))
  await expect(request('/test', z.object({}))).rejects.toBeInstanceOf(ApiError)
  await expect(request('/test', z.object({}))).rejects.toMatchObject({
    code: 'network_error',
    status: 0,
  })
})

it('preserves cancellation while reading a response body', async () => {
  const aborted = new DOMException('Aborted', 'AbortError')
  const response = json({})
  vi.spyOn(response, 'json').mockRejectedValue(aborted)
  mockApi(() => response)
  await expect(request('/test', z.object({}))).rejects.toBe(aborted)
})
