import { Link } from 'react-router-dom'
import { ArrowRight, AlertTriangle } from 'lucide-react'
import type { Shipment } from '@/types/api'
import { StatusBadge } from '@/components/shared/StatusBadge'
import { formatDate, isOverdue, kilograms } from '@/lib/dates'
export function ShipmentTable({
  shipments,
  compact = false,
  asOf,
}: {
  shipments: Shipment[]
  compact?: boolean
  asOf?: string
}) {
  return (
    <div
      className="table-scroll"
      tabIndex={0}
      role="region"
      aria-label="Shipments table"
    >
      <table
        className={`data-table shipment-table ${compact ? 'compact-table' : ''}`}
      >
        <caption className="sr-only">
          Shipments · times in Europe/Vienna
        </caption>
        <thead>
          <tr>
            <th>Shipment</th>
            <th>Customer</th>
            <th>Route</th>
            <th>Status</th>
            <th>Delivery deadline</th>
            {!compact && <th>Assignment</th>}
            <th>
              <span className="sr-only">Open</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {shipments.map((shipment) => {
            const overdue = isOverdue(
              shipment,
              asOf ? Date.parse(asOf) : undefined,
            )
            return (
              <tr key={shipment.id}>
                <td>
                  <Link
                    className="shipment-reference"
                    to={`/shipments/${shipment.id}`}
                  >
                    {shipment.shipmentNumber}
                  </Link>
                  <div className="cell-secondary">
                    {kilograms(shipment.weightKg)} · {shipment.palletCount}{' '}
                    pallets
                  </div>
                </td>
                <td>
                  <span className="cell-primary">
                    {shipment.customerName ??
                      'Customer details available in shipment'}
                  </span>
                </td>
                <td>
                  <div className="route-cell">
                    <span>{shipment.origin.city}</span>
                    <ArrowRight size={13} />
                    <span>{shipment.destination.city}</span>
                  </div>
                  <div className="cell-secondary">
                    {shipment.origin.countryCode} to{' '}
                    {shipment.destination.countryCode}
                  </div>
                </td>
                <td>
                  <StatusBadge status={shipment.status} />
                </td>
                <td>
                  <span className={overdue ? 'text-amber-700 font-medium' : ''}>
                    {formatDate(shipment.deliveryDeadlineUtc)}
                  </span>
                  {overdue && (
                    <span className="cell-secondary text-amber-700 flex items-center gap-1">
                      <AlertTriangle size={12} />
                      Overdue
                    </span>
                  )}
                </td>
                {!compact && (
                  <td>
                    {shipment.assignedDriverId ? (
                      <>
                        <span className="cell-primary">
                          {shipment.assignedDriverName ?? 'Assigned driver'}
                        </span>
                        <div className="cell-secondary">
                          {shipment.assignedVehiclePlateNumber ??
                            'Assigned vehicle'}
                        </div>
                      </>
                    ) : (
                      <span className="unassigned-label">Unassigned</span>
                    )}
                  </td>
                )}
                <td>
                  <Link
                    to={`/shipments/${shipment.id}`}
                    className="row-link"
                    aria-label={`View ${shipment.shipmentNumber}`}
                  >
                    <ArrowRight size={16} />
                  </Link>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
