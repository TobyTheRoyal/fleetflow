import type { QueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { keys } from '@/lib/queries'
export const notificationSchema = z.object({
  eventId: z.uuid(),
  entityType: z.enum(['shipment', 'customer', 'driver', 'vehicle']),
  entityId: z.uuid(),
  eventType: z.string().min(1).max(60),
  version: z.number().int().positive().nullable(),
  timestampUtc: z.iso.datetime({ offset: true }),
})
export async function refreshLogistics(cache: QueryClient) {
  await Promise.all(
    Object.values(keys).map((queryKey) =>
      cache.invalidateQueries({ queryKey }),
    ),
  )
}
export function notificationHandler(cache: QueryClient) {
  const seen = new Set<string>()
  const versions = new Map<string, number>()
  return (payload: unknown) => {
    const parsed = notificationSchema.safeParse(payload)
    if (!parsed.success) return
    const notification = parsed.data
    if (seen.has(notification.eventId)) return
    seen.add(notification.eventId)
    if (seen.size > 1000) seen.delete(seen.values().next().value!)
    if (notification.entityType === 'shipment') {
      const previous = versions.get(notification.entityId) ?? 0
      if (notification.version !== null) {
        if (notification.version <= previous) return
        versions.set(notification.entityId, notification.version)
        if (versions.size > 1000) versions.delete(versions.keys().next().value!)
      }
      // Invalidation never writes into an open form's captured values/version.
      void cache.invalidateQueries({ queryKey: keys.dashboard })
      void cache.invalidateQueries({ queryKey: keys.shipments })
    } else {
      const queryKey =
        notification.entityType === 'driver'
          ? keys.drivers
          : notification.entityType === 'vehicle'
            ? keys.vehicles
            : keys.customers
      void cache.invalidateQueries({ queryKey })
      // Detail DTOs contain resource names; refresh those projections as well.
      void cache.invalidateQueries({ queryKey: keys.shipments })
      void cache.invalidateQueries({ queryKey: keys.dashboard })
    }
  }
}
