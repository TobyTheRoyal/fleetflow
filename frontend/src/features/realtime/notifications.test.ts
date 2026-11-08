import { expect, it, vi } from 'vitest'
import { QueryClient } from '@tanstack/react-query'
import { notificationHandler, refreshLogistics } from './notifications'
const notification = {
  eventId: '11111111-1111-4111-8111-111111111111',
  entityType: 'shipment',
  entityId: '22222222-2222-4222-8222-222222222222',
  eventType: 'ShipmentStatusChanged',
  version: 3,
  timestampUtc: '2026-11-08T10:00:00Z',
}
it('invalidates shipment details, history, listings and dashboard without patching data', () => {
  const cache = new QueryClient()
  const invalidate = vi.spyOn(cache, 'invalidateQueries')
  cache.setQueryData(['shipments', notification.entityId], { version: 2 })
  notificationHandler(cache)(notification)
  expect(invalidate.mock.calls.map(([filter]) => filter?.queryKey)).toEqual([
    ['dashboard'],
    ['shipments'],
  ])
  expect(cache.getQueryData(['shipments', notification.entityId])).toEqual({
    version: 2,
  })
})
it('deduplicates events and ignores older shipment versions independently of timestamp order', () => {
  const cache = new QueryClient()
  const invalidate = vi.spyOn(cache, 'invalidateQueries')
  const receive = notificationHandler(cache)
  receive(notification)
  receive(notification)
  receive({
    ...notification,
    eventId: '33333333-3333-4333-8333-333333333333',
    version: 2,
    timestampUtc: '2026-11-08T11:00:00Z',
  })
  expect(invalidate).toHaveBeenCalledTimes(2)
  receive({
    ...notification,
    eventId: '44444444-4444-4444-8444-444444444444',
    version: 4,
    timestampUtc: '2026-11-08T09:00:00Z',
  })
  expect(invalidate).toHaveBeenCalledTimes(4)
})
it.each(['driver', 'vehicle', 'customer'] as const)(
  'refreshes %s projections for out-of-order unversioned hints',
  (entityType) => {
    const cache = new QueryClient()
    const invalidate = vi.spyOn(cache, 'invalidateQueries')
    const receive = notificationHandler(cache)
    receive({ ...notification, entityType, version: null })
    receive({
      ...notification,
      entityType,
      version: null,
      eventId: '33333333-3333-4333-8333-333333333333',
      timestampUtc: '2026-11-08T09:00:00Z',
    })
    expect(invalidate).toHaveBeenCalledTimes(6)
    expect(invalidate).toHaveBeenCalledWith({
      queryKey: [entityType === 'vehicle' ? 'vehicles' : entityType + 's'],
    })
  },
)
it('ignores malformed hints and recovers all logistics caches after a gap', async () => {
  const cache = new QueryClient()
  const invalidate = vi.spyOn(cache, 'invalidateQueries')
  const receive = notificationHandler(cache)
  receive({ ...notification, entityId: 'bad' })
  receive({ ...notification, version: -1 })
  expect(invalidate).not.toHaveBeenCalled()
  await refreshLogistics(cache)
  expect(invalidate).toHaveBeenCalledTimes(5)
})
