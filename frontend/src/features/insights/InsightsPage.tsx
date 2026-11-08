import {
  useQuery,
  useQueryClient,
  keepPreviousData,
} from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { RefreshCw, ShieldCheck } from 'lucide-react'
import { z } from 'zod'
import { PageHeader } from '@/components/shared/PageHeader'
import { EmptyState, ErrorNotice, Skeleton } from '@/components/shared/Feedback'
import { Pagination } from '@/components/shared/Pagination'
import { Button } from '@/components/ui/button'
import { request, queryString } from '@/lib/api'
import { usePaging } from '@/lib/paging'
import { formatDate, TIME_ZONE } from '@/lib/dates'
import { pageSchema } from '@/types/api'
import { findingSchema, summarySchema } from './contracts'
import { FindingCard } from './FindingCard'
import { AiAnalysisPanel } from './AiAnalysisPanel'
export function InsightsPage() {
  const cache = useQueryClient()
  const [params] = useSearchParams()
  const { page, pageSize, change } = usePaging()
  const filter = params.get('shipmentId')
  const shipmentId =
    filter && z.uuid().safeParse(filter).success ? filter : undefined
  const summary = useQuery({
    queryKey: ['insights', 'summary'],
    queryFn: ({ signal }) =>
      request('/api/v1/insights/summary', summarySchema, { signal }),
  })
  const findings = useQuery({
    queryKey: ['insights', 'list', shipmentId, page, pageSize],
    queryFn: ({ signal }) =>
      request(
        `/api/v1/insights?${queryString({ shipmentId, page, pageSize })}`,
        pageSchema(findingSchema),
        { signal },
      ),
    placeholderData: keepPreviousData,
  })
  const refreshing = summary.isFetching || findings.isFetching
  const metrics = summary.data
    ? ([
        ['Affected shipments', summary.data.affectedShipments],
        ['Overdue shipments', summary.data.overdueShipments],
        ['Due within 24 hours', summary.data.dueSoonShipments],
        ['Critical findings', summary.data.criticalFindings],
      ] as const)
    : []
  return (
    <>
      <PageHeader
        eyebrow="LOGISTICS INTELLIGENCE"
        title="AI Insights"
        description="Verified operational findings first. Optional AI assistance, under explicit usage limits."
        action={
          <Button
            variant="outline"
            disabled={refreshing}
            onClick={() =>
              void cache.invalidateQueries({ queryKey: ['insights'] })
            }
          >
            <RefreshCw size={16} className={refreshing ? 'animate-spin' : ''} />
            Refresh findings
          </Button>
        }
      />
      <div className="mb-5 flex flex-wrap items-center justify-between gap-3 text-xs text-slate-600">
        <span className="inline-flex items-center gap-2">
          <ShieldCheck size={15} className="text-emerald-700" />
          Verified operational findings · PostgreSQL
        </span>
        {summary.data && (
          <span>
            As of {formatDate(summary.data.asOfUtc)} · {TIME_ZONE}
          </span>
        )}
      </div>
      {summary.isError && (
        <ErrorNotice
          error={summary.error}
          retry={() => void summary.refetch()}
        />
      )}
      {summary.isPending ? (
        <Skeleton rows={2} />
      ) : (
        <div className="mb-7 grid grid-cols-2 gap-3 lg:grid-cols-4">
          {metrics.map(([label, value]) => (
            <div key={label} className="panel p-5">
              <p className="text-xs text-slate-600">{label}</p>
              <p className="mt-3 text-3xl font-semibold text-slate-900">
                {value}
              </p>
            </div>
          ))}
        </div>
      )}
      <div className="mb-7">
        <AiAnalysisPanel
          fingerprint={summary.data?.snapshotFingerprint}
          sampleSize={summary.data?.aiSampleSize}
          refreshing={summary.isFetching}
        />
      </div>
      <section aria-labelledby="operational-findings-heading">
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2
              id="operational-findings-heading"
              className="text-lg font-semibold text-slate-900"
            >
              Prioritized operational alerts
            </h2>
            <p className="mt-1 text-sm text-slate-600">
              Evidence is factual. Recommended next steps are suggestions for a
              dispatcher to review.
            </p>
          </div>
          {filter && (
            <Link
              to="/insights"
              className="text-sm font-semibold text-blue-700 hover:underline"
            >
              Show all shipments
            </Link>
          )}
        </div>
        {filter && !shipmentId && (
          <p role="alert" className="mb-4 text-sm text-amber-900">
            The shipment filter is invalid. Showing all findings.
          </p>
        )}
        {findings.isError && (
          <ErrorNotice
            error={findings.error}
            retry={() => void findings.refetch()}
          />
        )}
        {findings.isPending ? (
          <Skeleton rows={4} />
        ) : (
          findings.data && (
            <>
              <div className="space-y-4">
                {findings.data.items.map((finding) => (
                  <FindingCard key={finding.findingId} finding={finding} />
                ))}
              </div>
              {findings.data.items.length === 0 && (
                <div className="panel">
                  <EmptyState
                    title={
                      findings.data.totalCount
                        ? 'No findings on this page'
                        : 'No current operational alerts'
                    }
                    description="No issues match this view. This does not predict future performance or verify facts outside the recorded data."
                    action={
                      findings.data.totalCount ? (
                        <Button
                          variant="outline"
                          onClick={() => change({ page: 1 })}
                        >
                          Return to first page
                        </Button>
                      ) : undefined
                    }
                  />
                </div>
              )}
              <Pagination
                {...findings.data}
                busy={findings.isFetching}
                onPage={(value) => change({ page: value })}
                onSize={(value) => change({ pageSize: value, page: 1 })}
              />
            </>
          )
        )}
      </section>
    </>
  )
}
