import { useInfiniteQuery } from '@tanstack/react-query'
import { History, Plus, UserRound, CheckCircle2, Clock3 } from 'lucide-react'
import { ErrorNotice, EmptyState, Skeleton } from '@/components/shared/Feedback'
import { Button } from '@/components/ui/button'
import { request } from '@/lib/api'
import { formatDate, TIME_ZONE } from '@/lib/dates'
import { statusLabel } from '@/lib/status'
import { pageSchema, eventSchema } from '@/types/api'
export function ShipmentTimeline({ id }: { id: string }) {
  const events = useInfiniteQuery({
    queryKey: ['shipments', id, 'events'],
    initialPageParam: 1,
    queryFn: ({ pageParam, signal }) =>
      request(
        `/api/v1/shipments/${id}/events?page=${pageParam}&pageSize=20`,
        pageSchema(eventSchema),
        { signal },
      ),
    getNextPageParam: (page) =>
      page.page < page.totalPages ? page.page + 1 : undefined,
  })
  const timeline = events.data?.pages.flatMap((p) => p.items) ?? []
  return (
    <section className="panel" aria-labelledby="history-title">
      <div className="panel-heading">
        <div>
          <h2 id="history-title">Shipment timeline</h2>
          <p>Immutable activity history · {TIME_ZONE}</p>
        </div>
        <History size={20} className="text-slate-400" />
      </div>
      {events.isError && (
        <div className="p-6">
          <ErrorNotice
            error={events.error}
            retry={() => void events.refetch()}
          />
        </div>
      )}
      {events.isPending ? (
        <Skeleton rows={3} />
      ) : timeline.length === 0 ? (
        <EmptyState
          title="No events available"
          description="The timeline will show recorded shipment activity."
        />
      ) : (
        <ol className="timeline">
          {timeline.map((event) => (
            <li key={event.id}>
              <span
                className={`timeline-icon ${event.newStatus === 'Delivered' ? 'delivered' : ''}`}
              >
                {event.eventType === 'Created' ? (
                  <Plus size={15} />
                ) : event.eventType === 'AssignmentChanged' ? (
                  <UserRound size={15} />
                ) : event.newStatus === 'Delivered' ? (
                  <CheckCircle2 size={15} />
                ) : (
                  <Clock3 size={15} />
                )}
              </span>
              <div className="timeline-entry">
                <h3>
                  {event.eventType === 'Created'
                    ? 'Shipment created'
                    : event.eventType === 'AssignmentChanged'
                      ? 'Assignment changed'
                      : event.eventType === 'DetailsChanged'
                        ? 'Shipment details updated'
                        : `${event.oldStatus ? statusLabel[event.oldStatus] : ''} → ${event.newStatus ? statusLabel[event.newStatus] : ''}`}
                </h3>
                {event.message && <p>{event.message}</p>}
                <div className="timeline-meta">
                  <time dateTime={event.occurredAtUtc}>
                    {formatDate(event.occurredAtUtc)}
                  </time>
                  <span>by {event.actor}</span>
                </div>
              </div>
            </li>
          ))}
        </ol>
      )}
      {events.hasNextPage && (
        <div className="p-5 border-t">
          <Button
            variant="outline"
            onClick={() => void events.fetchNextPage()}
            disabled={events.isFetchingNextPage}
          >
            {events.isFetchingNextPage
              ? 'Loading…'
              : `Load more history (${timeline.length} of ${events.data?.pages[0]?.totalCount ?? 0})`}
          </Button>
        </div>
      )}
    </section>
  )
}
