import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'
export function Pagination({
  page,
  pageSize,
  totalCount,
  totalPages,
  onPage,
  onSize,
  busy = false,
}: {
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  onPage: (page: number) => void
  onSize: (size: number) => void
  busy?: boolean
}) {
  const start =
    totalCount === 0 || page > totalPages ? 0 : (page - 1) * pageSize + 1
  const end = start === 0 ? 0 : Math.min(page * pageSize, totalCount)
  return (
    <div className="pagination">
      <p className="text-sm text-slate-500">
        Showing{' '}
        <strong className="text-slate-700">
          {start}–{end}
        </strong>{' '}
        of <strong className="text-slate-700">{totalCount}</strong>
      </p>
      <div className="flex flex-wrap items-center gap-3">
        <label className="text-xs text-slate-500 flex items-center gap-2">
          Rows per page
          <select
            aria-label="Rows per page"
            className="control compact"
            value={pageSize}
            onChange={(e) => onSize(Number(e.target.value))}
            disabled={busy}
          >
            {[10, 20, 50, 100].map((size) => (
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </select>
        </label>
        <span className="text-xs text-slate-500">
          Page {page} of {Math.max(1, totalPages)}
        </span>
        <Button
          variant="outline"
          size="sm"
          aria-label="Previous page"
          disabled={busy || page <= 1}
          onClick={() => onPage(page - 1)}
        >
          <ChevronLeft size={16} />
        </Button>
        <Button
          variant="outline"
          size="sm"
          aria-label="Next page"
          disabled={busy || page >= totalPages}
          onClick={() => onPage(page + 1)}
        >
          <ChevronRight size={16} />
        </Button>
      </div>
    </div>
  )
}
