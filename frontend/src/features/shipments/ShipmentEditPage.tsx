import { useQuery } from '@tanstack/react-query'
import { useParams } from 'react-router-dom'
import { request } from '@/lib/api'
import { shipmentSchema } from '@/types/api'
import { Skeleton, ErrorNotice } from '@/components/shared/Feedback'
import { ShipmentForm } from './ShipmentForm'
export function ShipmentEditPage() {
  const { id } = useParams()
  const query = useQuery({
    queryKey: ['shipments', id],
    queryFn: ({ signal }) =>
      request(`/api/v1/shipments/${id}`, shipmentSchema, { signal }),
  })
  if (query.isPending) return <Skeleton />
  if (!query.data)
    return (
      <ErrorNotice error={query.error} retry={() => void query.refetch()} />
    )
  return (
    <>
      {query.isError && (
        <ErrorNotice error={query.error} retry={() => void query.refetch()} />
      )}
      <ShipmentForm key={query.data.id} initial={query.data} />
    </>
  )
}
