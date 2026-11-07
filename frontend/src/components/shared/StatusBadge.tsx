import type { ShipmentStatus } from '@/types/api'
import { statusLabel } from '@/lib/status'
export function StatusBadge({ status }: { status: ShipmentStatus }) {
  return (
    <span className={`status-badge status-${status.toLowerCase()}`}>
      <span className="status-dot" />
      {statusLabel[status]}
    </span>
  )
}
export function AvailabilityBadge({ value }: { value: string }) {
  return (
    <span
      className={`status-badge tone-${['Active', 'Available'].includes(value) ? 'green' : value === 'Maintenance' ? 'amber' : 'slate'}`}
    >
      <span className="status-dot" />
      {value}
    </span>
  )
}
