import { useCanManage } from '@/features/auth/context'
import { useState, useRef, type ReactNode } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Plus, Pencil } from 'lucide-react'
import { z } from 'zod'
import { PageHeader } from './PageHeader'
import { Pagination } from './Pagination'
import { ErrorNotice, EmptyState, Skeleton } from './Feedback'
import { Button } from '@/components/ui/button'
import { Dialog } from '@/components/ui/dialog'
import { request, queryString } from '@/lib/api'
import { usePaging } from '@/lib/paging'
import { pageSchema } from '@/types/api'
export function ResourcePage<T extends { id: string }>({
  resource,
  title,
  singular,
  description,
  schema,
  columns,
  renderForm,
  editable = false,
}: {
  resource: string
  title: string
  singular: string
  description: string
  schema: z.ZodType<T>
  columns: { label: string; render: (item: T) => ReactNode }[]
  renderForm: (
    item: T | undefined,
    close: () => void,
    busy: (value: boolean) => void,
  ) => ReactNode
  editable?: boolean
}) {
  const canManage = useCanManage()
  editable = editable && canManage
  const { page, pageSize, change } = usePaging()
  const query = useQuery({
    queryKey: [resource, 'list', page, pageSize],
    queryFn: ({ signal }) =>
      request(
        `/api/v1/${resource}?${queryString({ page, pageSize })}`,
        pageSchema(schema),
        { signal },
      ),
    placeholderData: keepPreviousData,
  })
  const trigger = useRef<HTMLElement | null>(null)
  const [editor, setEditor] = useState<{ item?: T } | null>(null)
  const [busy, setBusy] = useState(false)
  return (
    <>
      <PageHeader
        eyebrow="YOUR LOGISTICS NETWORK"
        title={title}
        description={description}
        action={
          canManage && (
            <Button
              onClick={(event) => {
                trigger.current = event.currentTarget
                setEditor({})
              }}
            >
              <Plus size={17} />
              Add {singular}
            </Button>
          )
        }
      />
      {query.isError && (
        <ErrorNotice error={query.error} retry={() => void query.refetch()} />
      )}
      <section className="panel">
        <div className="panel-heading">
          <div>
            <h2>{title} directory</h2>
            <p>
              {query.data
                ? `${query.data.totalCount} records`
                : 'Your operational resources'}
            </p>
          </div>
          {query.isFetching && !query.isPending && (
            <span className="field-hint" role="status">
              Updating…
            </span>
          )}
        </div>
        {!query.data && query.isPending ? (
          <Skeleton />
        ) : query.data && query.data.items.length > 0 ? (
          <div
            className="table-scroll"
            tabIndex={0}
            role="region"
            aria-label={`${title} table`}
          >
            <table className="data-table">
              <thead>
                <tr>
                  {columns.map((c) => (
                    <th key={c.label} scope="col">
                      {c.label}
                    </th>
                  ))}
                  {editable && (
                    <th scope="col">
                      <span className="sr-only">Actions</span>
                    </th>
                  )}
                </tr>
              </thead>
              <tbody>
                {query.data.items.map((item) => (
                  <tr key={item.id}>
                    {columns.map((c) => (
                      <td key={c.label}>{c.render(item)}</td>
                    ))}
                    {editable && (
                      <td className="text-right">
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={(event) => {
                            trigger.current = event.currentTarget
                            setEditor({ item })
                          }}
                        >
                          <Pencil size={14} />
                          Edit
                        </Button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          query.data && (
            <EmptyState
              title={
                query.data.totalCount > 0
                  ? `No ${title.toLowerCase()} on this page`
                  : `No ${title.toLowerCase()} here yet`
              }
              description={
                query.data.totalCount > 0
                  ? 'Results may have changed. Return to the first page to continue.'
                  : `Add a ${singular} to start building your logistics network.`
              }
              action={
                <>
                  {query.data.totalCount > 0 ? (
                    <Button onClick={() => change({ page: 1 })}>
                      Go to first page
                    </Button>
                  ) : canManage ? (
                    <Button
                      onClick={(event) => {
                        trigger.current = event.currentTarget
                        setEditor({})
                      }}
                    >
                      Add {singular}
                    </Button>
                  ) : null}
                </>
              }
            />
          )
        )}
        {query.data && (
          <Pagination
            page={query.data.page}
            pageSize={pageSize}
            totalCount={query.data.totalCount}
            totalPages={query.data.totalPages}
            busy={query.isFetching}
            onPage={(value) => change({ page: value })}
            onSize={(value) => change({ pageSize: value, page: 1 })}
          />
        )}
      </section>
      {editor && (
        <Dialog
          open
          onOpenChange={(open) => {
            if (!open) setEditor(null)
          }}
          title={`${editor.item ? 'Edit' : 'Add'} ${singular}`}
          description={`Manage ${singular} details for your FleetFlow workspace.`}
          busy={busy}
          returnFocusTo={trigger}
        >
          {renderForm(editor.item, () => setEditor(null), setBusy)}
        </Dialog>
      )}
    </>
  )
}
