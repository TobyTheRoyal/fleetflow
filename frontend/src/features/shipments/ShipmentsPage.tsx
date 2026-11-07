import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Filter, Plus, Search, X, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { PageHeader } from '@/components/shared/PageHeader'
import { EmptyState, ErrorNotice, Skeleton } from '@/components/shared/Feedback'
import { Pagination } from '@/components/shared/Pagination'
import { ShipmentTable } from './ShipmentTable'
import { usePaging } from '@/lib/paging'
import { useCustomerOptions } from '@/lib/queries'
import { request, queryString } from '@/lib/api'
import { pageSchema, shipmentSchema, statuses } from '@/types/api'
import { statusLabel } from '@/lib/status'
export function ShipmentsPage() {
  const { params, page, pageSize, change } = usePaging()
  const status = statuses.find((s) => s === params.get('status'))
  const customerId = params.get('customerId') ?? undefined
  const search = (params.get('search') ?? '').slice(0, 100)
  const deadline = ['Overdue', 'DueSoon'].includes(params.get('deadline') ?? '')
    ? params.get('deadline')!
    : undefined
  const filters = { page, pageSize, status, customerId, search, deadline }
  const query = useQuery({
    queryKey: ['shipments', 'list', filters],
    queryFn: ({ signal }) =>
      request(
        `/api/v1/shipments?${queryString(filters)}`,
        pageSchema(shipmentSchema),
        { signal },
      ),
    placeholderData: keepPreviousData,
  })
  const customers = useCustomerOptions()
  const options = customers.data?.pages.flatMap((p) => p.items) ?? []
  const hasFilters = !!(status || customerId || search || deadline)
  return (
    <>
      <PageHeader
        eyebrow="TRANSPORT MANAGEMENT"
        title="Shipments"
        description="Plan, dispatch and follow every shipment from pickup to delivery."
        action={
          <Button asChild>
            <Link to="/shipments/new">
              <Plus size={17} />
              New shipment
            </Link>
          </Button>
        }
      />
      <section className="panel">
        <div className="panel-heading">
          <div>
            <h2>
              All shipments{' '}
              {query.data && (
                <span className="count-chip">{query.data.totalCount}</span>
              )}
            </h2>
            <p>Newest first · all times in Europe/Vienna</p>
          </div>
          {query.isFetching && (
            <span className="updating">
              <RefreshCw size={13} className="animate-spin" />
              Updating
            </span>
          )}
        </div>
        <form
          key={params.toString()}
          className="filter-bar"
          onSubmit={(event) => {
            event.preventDefault()
            const form = new FormData(event.currentTarget)
            change({
              search: String(form.get('search') ?? '').trim(),
              status: String(form.get('status') ?? ''),
              customerId: String(form.get('customerId') ?? ''),
              page: 1,
            })
          }}
        >
          <div className="search-control">
            <Search size={16} />
            <input
              name="search"
              aria-label="Search shipments"
              placeholder="Search reference or city…"
              defaultValue={search}
              maxLength={100}
            />
          </div>
          <select
            name="status"
            aria-label="Filter by status"
            className="control"
            defaultValue={status ?? ''}
          >
            <option value="">All statuses</option>
            {statuses.map((s) => (
              <option key={s} value={s}>
                {statusLabel[s]}
              </option>
            ))}
          </select>
          <select
            name="customerId"
            aria-label="Filter by customer"
            className="control customer-filter"
            defaultValue={customerId ?? ''}
          >
            <option value="">All customers</option>
            {customerId && !options.some((c) => c.id === customerId) && (
              <option value={customerId}>Selected customer</option>
            )}
            {options.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
          <Button variant="outline" size="sm" type="submit">
            <Filter size={14} />
            Apply filters
          </Button>
          {hasFilters && (
            <Button
              variant="ghost"
              size="sm"
              onClick={() =>
                change({
                  search: undefined,
                  status: undefined,
                  customerId: undefined,
                  deadline: undefined,
                  page: 1,
                })
              }
            >
              <X size={14} />
              Clear
            </Button>
          )}
        </form>
        {customers.hasNextPage && (
          <button
            className="text-button px-6 pb-3"
            onClick={() => void customers.fetchNextPage()}
            disabled={customers.isFetchingNextPage}
          >
            Load more customers for filtering
          </button>
        )}
        {customers.isError && (
          <div className="px-6 pb-4">
            <ErrorNotice
              error={customers.error}
              retry={() => void customers.refetch()}
            />
          </div>
        )}
        {deadline && (
          <div className="filter-context">
            <span className="filter-pill">
              {deadline === 'Overdue'
                ? 'Overdue deliveries'
                : 'Due within 24 hours'}
              <button
                aria-label="Remove deadline filter"
                onClick={() => change({ deadline: undefined, page: 1 })}
              >
                <X size={12} />
              </button>
            </span>
            <span>
              Delivered and cancelled shipments are excluded from this view.
            </span>
          </div>
        )}
        {query.isError && (
          <div className="p-6">
            <ErrorNotice
              error={query.error}
              retry={() => void query.refetch()}
            />
          </div>
        )}
        {!query.data && query.isPending ? (
          <Skeleton />
        ) : (
          query.data && (
            <>
              <div
                className={query.isPlaceholderData ? 'opacity-50' : ''}
                aria-busy={query.isFetching}
              >
                {query.data.items.length ? (
                  <ShipmentTable shipments={query.data.items} />
                ) : (
                  <EmptyState
                    title={
                      hasFilters ? 'No matching shipments' : 'No shipments yet'
                    }
                    description={
                      hasFilters
                        ? 'Try a different search or clear your filters to see more shipments.'
                        : 'Create a transport order to start planning your deliveries.'
                    }
                    action={
                      hasFilters ? (
                        <Button
                          variant="outline"
                          onClick={() =>
                            change({
                              search: undefined,
                              status: undefined,
                              customerId: undefined,
                              deadline: undefined,
                              page: 1,
                            })
                          }
                        >
                          Clear filters
                        </Button>
                      ) : (
                        <Button asChild>
                          <Link to="/shipments/new">Create shipment</Link>
                        </Button>
                      )
                    }
                  />
                )}
              </div>
              <Pagination
                {...query.data}
                onPage={(next) => change({ page: next })}
                onSize={(size) => change({ pageSize: size, page: 1 })}
                busy={query.isPlaceholderData}
              />
            </>
          )
        )}
      </section>
    </>
  )
}
