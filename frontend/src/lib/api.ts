import { z } from 'zod'

const problemSchema = z.object({
  detail: z.string().optional(),
  title: z.string().optional(),
  code: z.string().optional(),
  traceId: z.string().optional(),
  errors: z.record(z.string(), z.array(z.string())).optional(),
})
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly traceId?: string
  readonly fields: Record<string, string[]>
  constructor(
    message: string,
    status: number,
    code: string,
    fields: Record<string, string[]> = {},
    traceId?: string,
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.fields = fields
    this.traceId = traceId
  }
}
export const apiBase = (import.meta.env.VITE_API_BASE_URL ?? '').replace(
  /\/$/,
  '',
)
export async function request<T>(
  path: string,
  schema: z.ZodType<T>,
  options: {
    signal?: AbortSignal
    method?: 'POST' | 'PUT'
    body?: unknown
  } = {},
): Promise<T> {
  let response: Response
  try {
    response = await fetch(apiBase + path, {
      signal: options.signal,
      method: options.method ?? 'GET',
      headers: {
        Accept: 'application/json',
        ...(options.body === undefined
          ? {}
          : { 'Content-Type': 'application/json' }),
      },
      ...(options.body === undefined
        ? {}
        : { body: JSON.stringify(options.body) }),
    })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError')
      throw error
    throw new ApiError(
      'Cannot reach FleetFlow. Check the API connection and try again.',
      0,
      'network_error',
    )
  }
  const body: unknown = await response.json().catch((error: unknown) => {
    if (error instanceof DOMException && error.name === 'AbortError')
      throw error
    return null
  })
  if (!response.ok) {
    const parsed = problemSchema.safeParse(body)
    const problem = parsed.success ? parsed.data : undefined
    throw new ApiError(
      problem?.detail ??
        (response.status === 503 || response.status === 502
          ? 'The service is temporarily unavailable. Please try again shortly.'
          : 'The request could not be completed. Please try again.'),
      response.status,
      problem?.code ?? 'request_failed',
      problem?.errors,
      problem?.traceId,
    )
  }
  const parsed = schema.safeParse(body)
  if (!parsed.success)
    throw new ApiError(
      'The API returned an unexpected response. Refresh the page or check the API version.',
      response.status,
      'invalid_response',
    )
  return parsed.data
}
export function queryString(
  values: Record<string, string | number | undefined>,
) {
  const params = new URLSearchParams()
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== '') params.set(key, String(value))
  })
  return params.toString()
}
