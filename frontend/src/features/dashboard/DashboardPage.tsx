import { useCanManage } from '@/features/auth/context'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  ArrowRight,
  ArrowUpRight,
  CheckCircle2,
  Clock3,
  Package,
  Plus,
  Truck,
  AlertTriangle,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { PageHeader } from '@/components/shared/PageHeader'
import { EmptyState, ErrorNotice, Skeleton } from '@/components/shared/Feedback'
import { ShipmentTable } from '@/features/shipments/ShipmentTable'
import { request } from '@/lib/api'
import { dashboardSchema, shipmentSchema, pageSchema } from '@/types/api'
import { formatDate, number } from '@/lib/dates'
import { statusLabel } from '@/lib/status'
export function DashboardPage() {
  const canManage = useCanManage()
  const query = useQuery({
    queryKey: ['dashboard'],
    queryFn: ({ signal }) =>
      request('/api/v1/dashboard/summary', dashboardSchema, { signal }),
  })
  const urgent = useQuery({
    queryKey: ['shipments', 'urgent'],
    queryFn: ({ signal }) =>
      request(
        '/api/v1/shipments?deadline=Overdue&pageSize=3',
        pageSchema(shipmentSchema),
        { signal },
      ),
  })
  const data = query.data
  const metrics = data
    ? [
        {
          label: 'Total shipments',
          count: data.totalNonCancelledShipments,
          note: 'Excluding cancelled · browse all',
          icon: Package,
          to: '/shipments',
          tone: 'blue',
        },
        {
          label: 'In transit',
          count: data.inTransitShipments,
          note: 'Currently on the road',
          icon: Truck,
          to: '/shipments?status=InTransit',
          tone: 'teal',
        },
        {
          label: 'Overdue',
          count: data.overdueShipments,
          note: 'Past the delivery deadline',
          icon: AlertTriangle,
          to: '/shipments?deadline=Overdue',
          tone: 'amber',
        },
        {
          label: 'Due within 24 hours',
          count: data.dueSoonShipments,
          note: 'Upcoming delivery deadlines',
          icon: Clock3,
          to: '/shipments?deadline=DueSoon',
          tone: 'violet',
        },
      ]
    : []
  const stages = data
    ? [
        { status: 'Draft' as const, count: data.draftShipments },
        { status: 'Planned' as const, count: data.plannedShipments },
        { status: 'InTransit' as const, count: data.inTransitShipments },
        { status: 'Delivered' as const, count: data.deliveredShipments },
      ]
    : []
  return (
    <>
      <PageHeader
        eyebrow="YOUR OPERATIONS, AT A GLANCE"
        title="Operations overview"
        description="Keep your shipments moving and your delivery priorities in focus."
        action={
          canManage && (
            <Button asChild>
              <Link to="/shipments/new">
                <Plus size={17} />
                New shipment
              </Link>
            </Button>
          )
        }
      />
      {query.isError && (
        <ErrorNotice error={query.error} retry={() => void query.refetch()} />
      )}
      {!data && query.isPending ? (
        <div className="panel">
          <Skeleton rows={6} />
        </div>
      ) : (
        data && (
          <>
            <div className="section-meta">
              <span className="live-label">
                <span className="health-dot" />
                {query.isFetching
                  ? 'Refreshing operations'
                  : 'Database-backed overview'}
              </span>
              <span>As of {formatDate(data.asOfUtc)} · Europe/Vienna</span>
            </div>
            <div className="kpi-grid">
              {metrics.map(({ label, count, note, icon: Icon, to, tone }) => (
                <Link key={label} to={to} className={`kpi-card kpi-${tone}`}>
                  <div className="flex items-center justify-between">
                    <span className="kpi-icon">
                      <Icon size={19} />
                    </span>
                    <ArrowUpRight size={15} className="text-slate-400" />
                  </div>
                  <p className="kpi-label">{label}</p>
                  <p className="kpi-number">{number.format(count)}</p>
                  <p className="kpi-note">{note}</p>
                </Link>
              ))}
            </div>
            <div className="dashboard-secondary">
              <section className="panel flow-panel">
                <div className="panel-heading">
                  <div>
                    <h2>Shipment flow</h2>
                    <p>A snapshot of your non-cancelled shipments</p>
                  </div>
                  <CheckCircle2 size={20} className="text-slate-400" />
                </div>
                <div className="flow-stages">
                  {stages.map((stage) => (
                    <Link
                      key={stage.status}
                      to={`/shipments?status=${stage.status}`}
                    >
                      <span
                        className={`flow-stage-dot status-${stage.status.toLowerCase()}`}
                      />
                      <span>{statusLabel[stage.status]}</span>
                      <strong>{stage.count}</strong>
                    </Link>
                  ))}
                </div>
                <div
                  className="distribution-bar"
                  aria-label="Shipment status distribution"
                >
                  {stages
                    .filter((s) => s.count > 0)
                    .map((stage) => (
                      <div
                        key={stage.status}
                        className={`distribution-${stage.status.toLowerCase()}`}
                        style={{
                          width: `${(stage.count / Math.max(data.totalNonCancelledShipments, 1)) * 100}%`,
                        }}
                        title={`${statusLabel[stage.status]}: ${stage.count}`}
                      />
                    ))}
                </div>
                <p className="panel-footnote">
                  Delivered shipments are included in the total. Cancelled
                  shipments are excluded.
                </p>
              </section>
              <section className="panel attention-panel">
                <div className="panel-heading">
                  <div>
                    <h2 className="flex items-center gap-2">
                      <AlertTriangle size={17} className="text-amber-600" />
                      Needs attention
                    </h2>
                    <p>Overdue deliveries that need a closer look</p>
                  </div>
                  <Link
                    className="text-button"
                    to="/shipments?deadline=Overdue"
                  >
                    View all
                    <ArrowRight size={14} />
                  </Link>
                </div>
                {urgent.isError ? (
                  <div className="p-5">
                    <ErrorNotice
                      error={urgent.error}
                      retry={() => void urgent.refetch()}
                    />
                  </div>
                ) : urgent.isPending ? (
                  <Skeleton rows={2} />
                ) : urgent.data.items.length === 0 ? (
                  <div className="attention-empty">
                    <CheckCircle2 size={19} />
                    <p>No overdue deliveries. You’re up to date.</p>
                  </div>
                ) : (
                  <div className="attention-list">
                    {urgent.data.items.map((s) => (
                      <Link key={s.id} to={`/shipments/${s.id}`}>
                        <span className="attention-mark" />
                        <div>
                          <strong>{s.shipmentNumber}</strong>
                          <span>
                            {s.origin.city} → {s.destination.city}
                          </span>
                        </div>
                        <span className="attention-deadline">
                          {formatDate(s.deliveryDeadlineUtc)}
                          <ArrowUpRight size={14} />
                        </span>
                      </Link>
                    ))}
                  </div>
                )}
              </section>
            </div>
            <section className="panel">
              <div className="panel-heading">
                <div>
                  <h2>Recent shipments</h2>
                  <p>Your most recently created shipments</p>
                </div>
                <Link className="text-button" to="/shipments">
                  All shipments
                  <ArrowRight size={15} />
                </Link>
              </div>
              {data.recentShipments.length > 0 ? (
                <ShipmentTable
                  shipments={data.recentShipments}
                  compact
                  asOf={data.asOfUtc}
                />
              ) : (
                <EmptyState
                  title="Your operation starts here"
                  description="Create your first shipment to see its progress and delivery priorities."
                  action={
                    canManage && (
                      <Button asChild>
                        <Link to="/shipments/new">Create shipment</Link>
                      </Button>
                    )
                  }
                />
              )}
            </section>
          </>
        )
      )}
    </>
  )
}
