import { createContext, useContext } from 'react'
import { z } from 'zod'
import type { components } from '@/types/api.generated'
export const sessionSchema = z.object({
  id: z.string().uuid(),
  email: z.string(),
  displayName: z.string(),
  role: z.enum(['Admin', 'Dispatcher', 'Viewer']),
  active: z.boolean(),
}) satisfies z.ZodType<components['schemas']['SessionUser']>
export type SessionUser = z.infer<typeof sessionSchema>
export type SessionState = {
  user: SessionUser | null
  ready: boolean
  locked: boolean
  signedIn: (user: SessionUser) => void
  logout: () => Promise<void>
}
export const SessionContext = createContext<SessionState | null>(null)
export function useSession() {
  const value = useContext(SessionContext)
  if (!value) throw new Error('SessionProvider is required.')
  return value
}
export function useCanManage() {
  const { user } = useSession()
  return user?.role === 'Admin' || user?.role === 'Dispatcher'
}
