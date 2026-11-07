import { useMutation, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { request } from './api'
export function useResourceWrite<T, Input>(
  resource: string,
  schema: z.ZodType<T>,
  id?: string,
) {
  const client = useQueryClient()
  return useMutation({
    retry: false,
    mutationFn: (body: Input) =>
      request(`/api/v1/${resource}${id ? '/' + id : ''}`, schema, {
        method: id ? 'PUT' : 'POST',
        body,
      }),
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: [resource] }),
        client.invalidateQueries({ queryKey: ['shipments'] }),
        client.invalidateQueries({ queryKey: ['dashboard'] }),
      ])
    },
  })
}
