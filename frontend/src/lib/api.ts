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
let csrf: Promise<string> | undefined
export function resetCsrf() {
  csrf = undefined
}
async function csrfToken() {
  csrf ??= fetch(apiBase + '/api/v1/auth/csrf', {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
  })
    .then(async (response) => {
      if (!response.ok)
        throw new ApiError(
          'Could not verify this session. Try again.',
          response.status,
          'csrf_unavailable',
        )
      return z.object({ token: z.string() }).parse(await response.json()).token
    })
    .catch((error) => {
      csrf = undefined
      throw error instanceof ApiError
        ? error
        : new ApiError(
            'Could not verify this session. Check the API connection and try again.',
            0,
            'csrf_unavailable',
          )
    })
  return csrf
}
export async function request<T>(
  path: string,
  schema: z.ZodType<T>,
  options: {
    signal?: AbortSignal
    method?: 'POST' | 'PUT'
    body?: unknown
  } = {},
): Promise<T> {
  const token = options.method ? await csrfToken() : undefined
  let response: Response
  try {
    response = await fetch(apiBase + path, {
      signal: options.signal,
      credentials: 'same-origin',
      method: options.method ?? 'GET',
      headers: {
        Accept: 'application/json',
        ...(token ? { 'X-CSRF-TOKEN': token } : {}),
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
      options.method
        ? 'Could not confirm the update. Check the record or shipment list before submitting again.'
        : 'Cannot reach FleetFlow. Check the API connection and try again.',
      0,
      options.method ? 'unknown_write_outcome' : 'network_error',
    )
  }
  const body: unknown = await response.json().catch((error: unknown) => {
    if (error instanceof DOMException && error.name === 'AbortError')
      throw error
    return null
  })
  if (!response.ok) {
    if (
      response.status === 401 &&
      ![
        '/api/v1/auth/login',
        '/api/v1/auth/logout',
        '/api/v1/auth/csrf',
      ].includes(path)
    ) {
      resetCsrf()
      window.dispatchEvent(new Event('fleetflow:unauthorized'))
    }
    const parsed = problemSchema.safeParse(body)
    const problem = parsed.success ? parsed.data : undefined
    if (problem?.code === 'csrf_invalid') resetCsrf()
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
      options.method
        ? 'The update may have been saved, but FleetFlow could not read the response. Check the record or shipment list before submitting again.'
        : 'The API returned an unexpected response. Refresh the page or check the API version.',
      response.status,
      options.method ? 'unknown_write_outcome' : 'invalid_response',
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
