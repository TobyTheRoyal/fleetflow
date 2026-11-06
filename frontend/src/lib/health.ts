export type HealthResponse = { status: 'Healthy' }

export class ApiError extends Error {
  readonly code: string
  readonly status: number

  constructor(message: string, code: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.status = status
  }
}

export async function getReadiness(
  signal?: AbortSignal,
): Promise<HealthResponse> {
  const response = await fetch('/health/ready', {
    signal,
    headers: { Accept: 'application/json' },
  })
  if (!response.ok) {
    const problem: unknown = await response.json().catch(() => null)
    const details =
      problem && typeof problem === 'object'
        ? (problem as Record<string, unknown>)
        : null
    throw new ApiError(
      typeof details?.detail === 'string'
        ? details.detail
        : 'Cannot reach the application. Check that the API and database are running.',
      typeof details?.code === 'string' ? details.code : 'connection_failed',
      response.status,
    )
  }

  const body: unknown = await response.json()
  if (
    !body ||
    typeof body !== 'object' ||
    !('status' in body) ||
    body.status !== 'Healthy'
  ) {
    throw new ApiError(
      'The API returned an unexpected health response.',
      'invalid_response',
      response.status,
    )
  }
  return { status: 'Healthy' }
}
