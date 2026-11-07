import { useSearchParams } from 'react-router-dom'
export function usePaging() {
  const [params, setParams] = useSearchParams()
  const requested = Number(params.get('page') ?? '1')
  const size = Number(params.get('pageSize') ?? '20')
  const page =
    Number.isInteger(requested) && requested > 0 && requested <= 1000000
      ? requested
      : 1
  const pageSize = [10, 20, 50, 100].includes(size) ? size : 20
  const change = (
    values: Record<string, string | number | undefined>,
    replace = false,
  ) => {
    const next = new URLSearchParams(params)
    Object.entries(values).forEach(([key, value]) => {
      if (value === undefined || value === '') next.delete(key)
      else next.set(key, String(value))
    })
    setParams(next, { replace })
  }
  return { params, page, pageSize, change }
}
