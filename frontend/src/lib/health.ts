import { z } from 'zod'
import { request } from './api'
export { ApiError } from './api'
export type HealthResponse = { status: 'Healthy' }
export function getReadiness(signal?: AbortSignal) {
  return request('/health/ready', z.object({ status: z.literal('Healthy') }), {
    signal,
  })
}
