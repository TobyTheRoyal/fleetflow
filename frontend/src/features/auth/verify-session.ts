import { ApiError, request, resetCsrf } from '@/lib/api'
import { sessionSchema } from './context'
export async function verifyCurrentUser(
  expectedId: string,
  signal?: AbortSignal,
) {
  const current = await request('/api/v1/auth/me', sessionSchema, { signal })
  // Another tab can replace the shared cookie without changing this tab's React state.
  // Lock the existing workspace; only an explicit login may adopt another account.
  if (current.id !== expectedId) {
    resetCsrf()
    window.dispatchEvent(new Event('fleetflow:unauthorized'))
    throw new ApiError(
      'The active account changed. Sign in again to continue.',
      401,
      'account_changed',
    )
  }
}
