import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Sparkles, LoaderCircle, Info } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { ErrorNotice, Skeleton } from '@/components/shared/Feedback'
import { ApiError, request } from '@/lib/api'
import { formatDate } from '@/lib/dates'
import { analysisSchema, availabilitySchema } from './contracts'
import { SeverityBadge } from './FindingCard'
export function AiAnalysisPanel({
  fingerprint,
  sampleSize,
  refreshing,
}: {
  fingerprint?: string
  sampleSize?: number
  refreshing: boolean
}) {
  const cache = useQueryClient()
  const availability = useQuery({
    queryKey: ['insights', 'ai-availability'],
    queryFn: ({ signal }) =>
      request('/api/v1/insights/ai-availability', availabilitySchema, {
        signal,
      }),
  })
  const generation = useMutation({
    retry: false,
    mutationFn: () =>
      request('/api/v1/insights/ai-analysis', analysisSchema, {
        method: 'POST',
        body: { snapshotFingerprint: fingerprint },
      }),
    onSettled: async (_data, error) => {
      if (error instanceof ApiError && error.code === 'insights_changed')
        await cache.invalidateQueries({ queryKey: ['insights'] })
      else
        await cache.invalidateQueries({
          queryKey: ['insights', 'ai-availability'],
        })
    },
  })
  const data = availability.data
  const result = generation.data
  const changed = result && result.snapshotFingerprint !== fingerprint
  const quota =
    result?.budget && availability.isFetching ? result.budget : data?.budget
  return (
    <section className="panel p-5 sm:p-6" aria-labelledby="ai-analysis-heading">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="eyebrow">OPTIONAL · ON DEMAND</p>
          <h2
            id="ai-analysis-heading"
            className="mt-2 flex items-center gap-2 text-lg font-semibold"
          >
            <Sparkles size={19} className="text-blue-700" />
            AI-assisted analysis
          </h2>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-600">
            The AI model prioritizes a bounded sample and selects approved next
            steps. Verified facts and readable explanations come from FleetFlow.
            Suggestions never change a shipment.
          </p>
        </div>
        <Button
          disabled={
            !data?.canGenerate ||
            !fingerprint ||
            !sampleSize ||
            refreshing ||
            availability.isFetching ||
            generation.isPending
          }
          onClick={() => {
            if (!generation.isPending) generation.mutate()
          }}
        >
          {generation.isPending ? (
            <LoaderCircle size={16} className="animate-spin" />
          ) : (
            <Sparkles size={16} />
          )}
          {generation.isPending
            ? 'Generating analysis…'
            : 'Generate AI Analysis'}
        </Button>
      </div>
      {availability.isPending && <Skeleton rows={1} />}
      {availability.isError && (
        <ErrorNotice
          error={availability.error}
          retry={() => void availability.refetch()}
        />
      )}
      {quota && (
        <div
          className="mt-5 flex flex-wrap gap-x-8 gap-y-2 rounded-lg bg-slate-50 px-4 py-3 text-sm text-slate-700"
          aria-label="Daily AI allowance"
        >
          <p>
            <strong>{quota.userRemaining}</strong> of {quota.userLimit} personal
            requests remaining
          </p>
          <p>
            <strong>{quota.globalRemaining}</strong> of {quota.globalLimit}{' '}
            global requests remaining
          </p>
          <p className="text-slate-600">Resets at 00:00 UTC</p>
        </div>
      )}
      {data && !data.canGenerate && (
        <div
          className="mt-4 flex gap-2 rounded-lg border border-slate-200 p-4 text-sm leading-6 text-slate-600"
          role="status"
        >
          <Info size={17} className="mt-1 shrink-0" />
          <p>{data.explanation}</p>
        </div>
      )}
      {generation.isError && (
        <div className="mt-4">
          <ErrorNotice error={generation.error} />
        </div>
      )}
      {generation.isPending && (
        <p className="mt-4 text-sm text-slate-600" role="status">
          One explicit request is in progress. There are no automatic retries.
        </p>
      )}
      {result && (
        <div className="mt-6 space-y-4">
          {changed && (
            <p
              role="status"
              className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900"
            >
              Operational data changed. This analysis describes an earlier
              snapshot. Review current findings before explicitly generating
              again.
            </p>
          )}
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-blue-700">
              AI-assisted suggestions · human review required
            </p>
            <p className="mt-2 text-sm leading-6 text-slate-700">
              {result.summary}
            </p>
            <p className="mt-2 text-xs text-slate-600">
              {result.model} · {formatDate(result.generatedAtUtc)}
              {result.cached ? ' · Cached result; no new provider request' : ''}
            </p>
          </div>
          {result.recommendations.map((item) => (
            <article
              key={item.findingId}
              className="rounded-lg border border-slate-200 p-4"
            >
              <div className="flex flex-wrap items-center gap-3">
                <SeverityBadge severity={item.severity} />
                <Link
                  className="text-sm font-semibold text-blue-700 hover:underline"
                  to={`/shipments/${item.shipmentId}`}
                >
                  {item.shipmentNumber}
                </Link>
              </div>
              <p className="mt-3 text-sm text-slate-700">
                <strong>Verified fact:</strong> {item.verifiedExplanation}
              </p>
              <p className="mt-2 text-sm text-slate-600">
                <strong>AI-selected suggestion:</strong> {item.suggestedAction}
              </p>
            </article>
          ))}
        </div>
      )}
    </section>
  )
}
