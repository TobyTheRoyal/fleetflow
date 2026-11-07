import { ShipmentTimeline } from './ShipmentTimeline'
import { statusLabel } from '@/lib/status'
import { useState, useRef } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import {
  ArrowLeft,
  ArrowRight,
  Pencil,
  Truck,
  CheckCircle2,
  MapPin,
  UserRound,
  Package,
  CalendarDays,
  Plus,
  X,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { ErrorNotice, Skeleton } from '@/components/shared/Feedback'
import { StatusBadge } from '@/components/shared/StatusBadge'
import { formatDate, kilograms, TIME_ZONE } from '@/lib/dates'
import { request } from '@/lib/api'
import { shipmentSchema } from '@/types/api'
import type { Shipment, ShipmentStatus } from '@/types/api'
import { AssignmentDialog, StatusDialog } from './ShipmentActions'
const next: Partial<
  Record<ShipmentStatus, { status: ShipmentStatus; label: string }>
> = {
  Draft: { status: 'Planned', label: 'Plan shipment' },
  Planned: { status: 'InTransit', label: 'Start transit' },
  InTransit: { status: 'Delivered', label: 'Mark delivered' },
}
export function ShipmentDetailPage() {
  const { id } = useParams()
  const query = useQuery({
    queryKey: ['shipments', id],
    queryFn: ({ signal }) =>
      request(`/api/v1/shipments/${id}`, shipmentSchema, { signal }),
  })
  const trigger = useRef<HTMLElement | null>(null)
  const [dialog, setDialog] = useState<{
    type: 'assign' | 'status'
    shipment: Shipment
    target?: ShipmentStatus
  } | null>(null)
  const shipment = query.data
  if (!shipment && query.isPending) return <Skeleton rows={8} />
  if (!shipment)
    return (
      <ErrorNotice error={query.error} retry={() => void query.refetch()} />
    )
  const editable = ['Draft', 'Planned'].includes(shipment.status)
  const action = next[shipment.status]
  return (
    <>
      <Link className="back-link" to="/shipments">
        <ArrowLeft size={15} />
        Back to shipments
      </Link>
      <div className="detail-header">
        <div>
          <p className="eyebrow">SHIPMENT DETAILS</p>
          <div className="flex flex-wrap items-center gap-4">
            <h1>{shipment.shipmentNumber}</h1>
            <StatusBadge status={shipment.status} />
          </div>
          <p className="page-description">
            {shipment.customerName ?? 'Customer'} · Created{' '}
            {formatDate(shipment.createdAtUtc)}
          </p>
        </div>
        <div className="detail-actions">
          {editable && (
            <Button variant="outline" asChild>
              <Link to={`/shipments/${id}/edit`}>
                <Pencil size={15} />
                Edit shipment
              </Link>
            </Button>
          )}
          {action && (
            <Button
              disabled={
                shipment.status === 'Draft' &&
                (!shipment.assignedDriverId || !shipment.assignedVehicleId)
              }
              onClick={(event) => {
                trigger.current = event.currentTarget
                setDialog({ type: 'status', shipment, target: action.status })
              }}
            >
              {action.status === 'Delivered' ? (
                <CheckCircle2 size={16} />
              ) : (
                <Truck size={16} />
              )}
              {action.label}
            </Button>
          )}
        </div>
      </div>
      {query.isError && (
        <ErrorNotice error={query.error} retry={() => void query.refetch()} />
      )}
      <div className="workflow-strip" aria-label="Shipment progress">
        {(['Draft', 'Planned', 'InTransit', 'Delivered'] as const).map(
          (stage, index) => (
            <div
              key={stage}
              className={`workflow-step ${['Draft', 'Planned', 'InTransit', 'Delivered'].indexOf(shipment.status) >= index ? 'complete' : ''}`}
            >
              <span>
                {['Draft', 'Planned', 'InTransit', 'Delivered'].indexOf(
                  shipment.status,
                ) > index ? (
                  <CheckCircle2 size={16} />
                ) : (
                  index + 1
                )}
              </span>
              <strong>{statusLabel[stage]}</strong>
              {index < 3 && <ArrowRight size={15} />}
            </div>
          ),
        )}
        {shipment.status === 'Cancelled' && (
          <span className="cancelled-progress">Shipment cancelled</span>
        )}
      </div>
      <div className="detail-grid">
        <div className="detail-main">
          <section className="panel">
            <div className="panel-heading">
              <div>
                <h2>Route & schedule</h2>
                <p>Pickup and delivery details · {TIME_ZONE}</p>
              </div>
              <MapPin size={20} className="text-slate-400" />
            </div>
            <div className="route-detail">
              {(['origin', 'destination'] as const).map((side) => (
                <div key={side} className="route-stop">
                  <span className={`route-stop-dot ${side}`} />
                  <div>
                    <p className="eyebrow">
                      {side === 'origin' ? 'PICKUP' : 'DELIVERY'}
                    </p>
                    <h3>
                      {shipment[side].city}, {shipment[side].countryCode}
                    </h3>
                    <p>
                      {shipment[side].street}
                      <br />
                      {shipment[side].postcode} {shipment[side].city}
                    </p>
                    <div className="schedule-label">
                      <CalendarDays size={14} />
                      <span>
                        {side === 'origin'
                          ? 'Scheduled pickup'
                          : 'Delivery deadline'}
                        <strong>
                          {formatDate(
                            side === 'origin'
                              ? shipment.pickupWindowStartUtc
                              : shipment.deliveryDeadlineUtc,
                          )}
                        </strong>
                      </span>
                    </div>
                  </div>
                </div>
              ))}
            </div>
            {(shipment.actualPickupAtUtc || shipment.actualDeliveredAtUtc) && (
              <div className="actual-times">
                <div>
                  <span>Actual pickup</span>
                  <strong>{formatDate(shipment.actualPickupAtUtc)}</strong>
                </div>
                <div>
                  <span>Actual delivery</span>
                  <strong>{formatDate(shipment.actualDeliveredAtUtc)}</strong>
                </div>
              </div>
            )}
          </section>
          <section className="panel">
            <div className="panel-heading">
              <div>
                <h2>Cargo details</h2>
                <p>Load information and handling notes</p>
              </div>
              <Package size={20} className="text-slate-400" />
            </div>
            <div className="cargo-summary">
              <div>
                <span>Total weight</span>
                <strong>{kilograms(shipment.weightKg)}</strong>
              </div>
              <div>
                <span>Pallet count</span>
                <strong>{shipment.palletCount}</strong>
              </div>
            </div>
            <p className="cargo-description">
              {shipment.cargoDescription || 'No additional cargo description.'}
            </p>
          </section>
          <ShipmentTimeline id={shipment.id} />
        </div>
        <aside className="detail-aside">
          <section className="panel">
            <div className="panel-heading">
              <div>
                <h2>Dispatch assignment</h2>
                <p>Driver and vehicle for this transport</p>
              </div>
            </div>
            <div className="assignment-summary">
              <div className="assignment-item">
                <span className="assignment-icon">
                  <UserRound size={19} />
                </span>
                <div>
                  <span>Driver</span>
                  <strong>
                    {shipment.assignedDriverName ?? 'Not assigned'}
                  </strong>
                </div>
              </div>
              <div className="assignment-item">
                <span className="assignment-icon">
                  <Truck size={19} />
                </span>
                <div>
                  <span>Vehicle</span>
                  <strong>
                    {shipment.assignedVehiclePlateNumber ?? 'Not assigned'}
                  </strong>
                </div>
              </div>
              {editable && (
                <Button
                  variant="outline"
                  className="w-full mt-5"
                  onClick={(event) => {
                    trigger.current = event.currentTarget
                    setDialog({ type: 'assign', shipment })
                  }}
                >
                  <Plus size={15} />
                  {shipment.assignedDriverId
                    ? 'Change assignment'
                    : 'Assign driver & vehicle'}
                </Button>
              )}
              {shipment.status === 'Draft' && !shipment.assignedDriverId && (
                <p className="field-hint mt-3">
                  Assign a driver and vehicle before planning.
                </p>
              )}
            </div>
          </section>
          <section className="panel p-5">
            <p className="eyebrow">CUSTOMER</p>
            <h2 className="mt-3 font-semibold">
              {shipment.customerName ?? 'Selected customer'}
            </h2>
            <Link to="/customers" className="text-button mt-4">
              Customer directory
              <ArrowRight size={14} />
            </Link>
          </section>
          <section className="panel p-5">
            <p className="eyebrow">RECORD DETAILS</p>
            <div className="record-meta">
              <span>
                Version<strong>{shipment.version}</strong>
              </span>
              <span>
                Last updated<strong>{formatDate(shipment.updatedAtUtc)}</strong>
              </span>
            </div>
            <p className="field-hint">
              Version checks protect changes made by other operators.
            </p>
            {editable && (
              <button
                className="text-button text-red-700 mt-5"
                onClick={(event) => {
                  trigger.current = event.currentTarget
                  setDialog({ type: 'status', shipment, target: 'Cancelled' })
                }}
              >
                <X size={14} />
                Cancel shipment
              </button>
            )}
          </section>
        </aside>
      </div>
      {dialog?.type === 'assign' && (
        <AssignmentDialog
          returnFocusTo={trigger}
          shipment={dialog.shipment}
          close={() => setDialog(null)}
        />
      )}
      {dialog?.type === 'status' && dialog.target && (
        <StatusDialog
          returnFocusTo={trigger}
          shipment={dialog.shipment}
          target={dialog.target}
          close={() => setDialog(null)}
        />
      )}
    </>
  )
}
