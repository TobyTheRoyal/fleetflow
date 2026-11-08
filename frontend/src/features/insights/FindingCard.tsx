import { Link } from 'react-router-dom'
import { ArrowUpRight, ShieldCheck } from 'lucide-react'
import { formatDate, TIME_ZONE } from '@/lib/dates'
import { statusLabel } from '@/lib/status'
import type { Finding } from './contracts'
const typeLabel: Record<Finding['type'], string> = {
  Overdue: 'Delivery overdue',
  InTransitOverdue: 'In transit past deadline',
  DueSoon: 'Delivery due within 24 hours',
  PickupWindowOpen: 'Pickup window started',
  UnassignedDraft: 'Assignment needed',
  AssignmentRequiresReview: 'Review resource eligibility',
}
export function SeverityBadge({ severity }: { severity: Finding['severity'] }) {
  const tone =
    severity === 'Critical'
      ? 'bg-rose-50 text-rose-800 border-rose-200'
      : severity === 'High'
        ? 'bg-amber-50 text-amber-900 border-amber-200'
        : severity === 'Medium'
          ? 'bg-blue-50 text-blue-800 border-blue-200'
          : 'bg-slate-50 text-slate-700 border-slate-200'
  return (
    <span
      className={`inline-flex rounded-md border px-2 py-1 text-xs font-semibold ${tone}`}
    >
      {severity}
    </span>
  )
}
export function FindingCard({ finding }: { finding: Finding }) {
  const evidence = finding.evidence
  return (
    <article className="panel p-5 sm:p-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <SeverityBadge severity={finding.severity} />
          <h3 className="font-semibold text-slate-900">
            {typeLabel[finding.type]}
          </h3>
        </div>
        <Link
          className="inline-flex items-center gap-1 text-sm font-semibold text-blue-700 hover:underline"
          to={`/shipments/${finding.shipmentId}`}
        >
          {finding.shipmentNumber}
          <ArrowUpRight size={15} />
          <span className="sr-only"> shipment details</span>
        </Link>
      </div>
      <p className="mt-3 text-sm leading-6 text-slate-700">
        {finding.explanation}
      </p>
      <details className="mt-4 rounded-lg border border-slate-200 p-3">
        <summary className="cursor-pointer text-sm font-medium text-slate-700">
          Supporting evidence
        </summary>
        <dl className="mt-3 grid gap-3 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-slate-600">Recorded status</dt>
            <dd>
              {statusLabel[evidence.status]} · Version {evidence.version}
            </dd>
          </div>
          <div>
            <dt className="text-slate-600">Delivery deadline</dt>
            <dd>{formatDate(evidence.deadlineUtc)}</dd>
          </div>
          <div>
            <dt className="text-slate-600">Pickup window starts</dt>
            <dd>{formatDate(evidence.pickupUtc)}</dd>
          </div>
          <div>
            <dt className="text-slate-600">Selected resources</dt>
            <dd>
              Driver:{' '}
              {evidence.driverAssigned
                ? evidence.driverEligible
                  ? 'eligible'
                  : 'review required'
                : 'not assigned'}{' '}
              · Vehicle:{' '}
              {evidence.vehicleAssigned
                ? evidence.vehicleEligible
                  ? 'eligible'
                  : 'review required'
                : 'not assigned'}
            </dd>
          </div>
        </dl>
        <p className="mt-3 text-xs text-slate-600">
          Verified at {formatDate(finding.timestampUtc)} · {TIME_ZONE}. No GPS,
          traffic, weather or delay-cause data is available.
        </p>
      </details>
      <div className="mt-4 flex gap-2 text-sm leading-6 text-slate-600">
        <ShieldCheck size={17} className="mt-1 shrink-0 text-blue-700" />
        <p>
          <strong className="text-slate-800">Suggested next step:</strong>{' '}
          {finding.recommendedAction}
        </p>
      </div>
    </article>
  )
}
