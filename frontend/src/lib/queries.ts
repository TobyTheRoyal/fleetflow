import {
  useInfiniteQuery,
  useMutation,
  useQueryClient,
} from '@tanstack/react-query'
import { request, queryString } from './api'
import {
  customerSchema,
  driverSchema,
  vehicleSchema,
  pageSchema,
  shipmentSchema,
} from '@/types/api'
import type { CreateShipment, UpdateShipment } from '@/types/api'
export const keys = {
  dashboard: ['dashboard'] as const,
  insights: ['insights'] as const,
  shipments: ['shipments'] as const,
  customers: ['customers'] as const,
  drivers: ['drivers'] as const,
  vehicles: ['vehicles'] as const,
}
export function useCustomerOptions() {
  return useInfiniteQuery({
    queryKey: ['customers', 'options'],
    initialPageParam: 1,
    queryFn: ({ pageParam, signal }) =>
      request(
        `/api/v1/customers?${queryString({ page: pageParam, pageSize: 100 })}`,
        pageSchema(customerSchema),
        { signal },
      ),
    getNextPageParam: (page) =>
      page.page < page.totalPages ? page.page + 1 : undefined,
  })
}
export function useDriverOptions() {
  return useInfiniteQuery({
    queryKey: ['drivers', 'options'],
    initialPageParam: 1,
    queryFn: ({ pageParam, signal }) =>
      request(
        `/api/v1/drivers?${queryString({ page: pageParam, pageSize: 100 })}`,
        pageSchema(driverSchema),
        { signal },
      ),
    getNextPageParam: (page) =>
      page.page < page.totalPages ? page.page + 1 : undefined,
  })
}
export function useVehicleOptions() {
  return useInfiniteQuery({
    queryKey: ['vehicles', 'options'],
    initialPageParam: 1,
    queryFn: ({ pageParam, signal }) =>
      request(
        `/api/v1/vehicles?${queryString({ page: pageParam, pageSize: 100 })}`,
        pageSchema(vehicleSchema),
        { signal },
      ),
    getNextPageParam: (page) =>
      page.page < page.totalPages ? page.page + 1 : undefined,
  })
}
export function useShipmentWrite(id?: string) {
  const client = useQueryClient()
  return useMutation({
    retry: false,
    mutationFn: (
      command:
        | { action: 'create'; body: CreateShipment }
        | { action: 'update'; body: UpdateShipment }
        | {
            action: 'assign'
            body: { version: number; driverId: string; vehicleId: string }
          }
        | {
            action: 'status'
            body: {
              version: number
              status: import('@/types/api').ShipmentStatus
              reason: string | null
            }
          },
    ) => {
      const path =
        command.action === 'create'
          ? '/api/v1/shipments'
          : `/api/v1/shipments/${id}${command.action === 'update' ? '' : '/' + command.action}`
      return request(path, shipmentSchema, {
        method: command.action === 'update' ? 'PUT' : 'POST',
        body: command.body,
      })
    },
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: keys.shipments }),
        client.invalidateQueries({ queryKey: keys.dashboard }),
        client.invalidateQueries({ queryKey: keys.insights }),
      ])
    },
  })
}
