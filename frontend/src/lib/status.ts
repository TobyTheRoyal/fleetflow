import type { ShipmentStatus } from '@/types/api'
export const statusLabel: Record<ShipmentStatus, string> = {
  Draft: 'Draft',
  Planned: 'Planned',
  InTransit: 'In transit',
  Delivered: 'Delivered',
  Cancelled: 'Cancelled',
}
