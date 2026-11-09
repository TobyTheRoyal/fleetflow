import { z } from 'zod'
export const demoAvailabilitySchema = z.object({
  enabled: z.boolean(),
  viewerAvailable: z.number().int().nonnegative(),
  dispatcherAvailable: z.number().int().nonnegative(),
  leaseMinutes: z.number().int().positive(),
})
