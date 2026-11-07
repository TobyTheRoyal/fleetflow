import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { toast } from 'sonner'
import { useQueryClient } from '@tanstack/react-query'
import { Dialog } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/shared/Field'
import { ErrorNotice, StaleNotice } from '@/components/shared/Feedback'
import { ApiError } from '@/lib/api'
import {
  useDriverOptions,
  useShipmentWrite,
  useVehicleOptions,
} from '@/lib/queries'
import { kilograms } from '@/lib/dates'
import type { Shipment } from '@/types/api'
const assignment = z.object({
  driverId: z.uuid({ error: 'Select a driver.' }),
  vehicleId: z.uuid({ error: 'Select a vehicle.' }),
})
export function AssignmentDialog({
  shipment,
  close,
}: {
  shipment: Shipment
  close: () => void
}) {
  const drivers = useDriverOptions()
  const vehicles = useVehicleOptions()
  const mutation = useShipmentWrite(shipment.id)
  const client = useQueryClient()
  const [failure, setFailure] = useState<unknown>(null)
  const [refreshing, setRefreshing] = useState(false)
  const form = useForm<z.infer<typeof assignment>>({
    resolver: zodResolver(assignment),
    defaultValues: {
      driverId: shipment.assignedDriverId ?? '',
      vehicleId: shipment.assignedVehicleId ?? '',
    },
  })
  const driverOptions = drivers.data?.pages.flatMap((p) => p.items) ?? []
  const vehicleOptions = vehicles.data?.pages.flatMap((p) => p.items) ?? []
  const stale = failure instanceof ApiError && failure.code === 'stale_version'
  const reload = async () => {
    setRefreshing(true)
    try {
      await client.invalidateQueries(
        { queryKey: ['shipments', shipment.id] },
        { throwOnError: true },
      )
      close()
    } catch (error) {
      toast.error(
        error instanceof Error
          ? error.message
          : 'Could not reload the shipment. Try again.',
      )
    } finally {
      setRefreshing(false)
    }
  }
  const submit = form.handleSubmit(async (values) => {
    if (mutation.isPending || stale) return
    setFailure(null)
    try {
      await mutation.mutateAsync({
        action: 'assign',
        body: { ...values, version: shipment.version },
      })
      toast.success('Assignment saved')
      close()
    } catch (error) {
      setFailure(error)
    }
  })
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) close()
      }}
      title="Assign driver & vehicle"
      description={`${shipment.shipmentNumber} · ${kilograms(shipment.weightKg)} · version ${shipment.version}`}
      busy={mutation.isPending}
    >
      <form noValidate onSubmit={submit} className="dialog-body space-y-5">
        {stale ? (
          <StaleNotice onReload={() => void reload()} busy={refreshing} />
        ) : failure ? (
          <ErrorNotice error={failure} />
        ) : null}
        <Field label="Driver" error={form.formState.errors.driverId?.message}>
          <select className="control" {...form.register('driverId')}>
            <option value="">
              {drivers.isPending ? 'Loading drivers…' : 'Select a driver'}
            </option>
            {shipment.assignedDriverId &&
              !driverOptions.some(
                (d) => d.id === shipment.assignedDriverId,
              ) && (
                <option value={shipment.assignedDriverId}>
                  {shipment.assignedDriverName ?? 'Current driver'} (current)
                </option>
              )}
            {driverOptions.map((driver) => (
              <option
                value={driver.id}
                key={driver.id}
                disabled={!driver.active}
              >
                {driver.firstName} {driver.lastName} · {driver.employeeNumber}
                {!driver.active ? ' · Inactive' : ''}
              </option>
            ))}
          </select>
        </Field>
        {drivers.hasNextPage && (
          <button
            type="button"
            className="text-button"
            disabled={drivers.isFetchingNextPage}
            onClick={() => void drivers.fetchNextPage()}
          >
            Load more drivers
          </button>
        )}
        {drivers.isError && (
          <ErrorNotice
            error={drivers.error}
            retry={() => void drivers.refetch()}
          />
        )}
        <Field label="Vehicle" error={form.formState.errors.vehicleId?.message}>
          <select className="control" {...form.register('vehicleId')}>
            <option value="">
              {vehicles.isPending ? 'Loading vehicles…' : 'Select a vehicle'}
            </option>
            {shipment.assignedVehicleId &&
              !vehicleOptions.some(
                (v) => v.id === shipment.assignedVehicleId,
              ) && (
                <option value={shipment.assignedVehicleId}>
                  {shipment.assignedVehiclePlateNumber ?? 'Current vehicle'}{' '}
                  (current)
                </option>
              )}
            {vehicleOptions.map((vehicle) => (
              <option
                value={vehicle.id}
                key={vehicle.id}
                disabled={
                  vehicle.status !== 'Available' ||
                  vehicle.maxPayloadKg < shipment.weightKg
                }
              >
                {vehicle.plateNumber} · {kilograms(vehicle.maxPayloadKg)} ·{' '}
                {vehicle.status}
                {vehicle.maxPayloadKg < shipment.weightKg
                  ? ' · Insufficient payload'
                  : ''}
              </option>
            ))}
          </select>
        </Field>
        {vehicles.hasNextPage && (
          <button
            type="button"
            className="text-button"
            disabled={vehicles.isFetchingNextPage}
            onClick={() => void vehicles.fetchNextPage()}
          >
            Load more vehicles
          </button>
        )}
        {vehicles.isError && (
          <ErrorNotice
            error={vehicles.error}
            retry={() => void vehicles.refetch()}
          />
        )}
        <div className="info-note">
          Only active drivers and available vehicles with enough payload can be
          selected. Scheduled reservations are checked by the server when you
          submit. Choose another resource if a conflict is reported.
        </div>
        <div className="dialog-actions">
          <Button
            variant="outline"
            onClick={close}
            disabled={mutation.isPending}
          >
            Cancel
          </Button>
          <Button
            type="submit"
            disabled={
              mutation.isPending ||
              stale ||
              refreshing ||
              drivers.isPending ||
              vehicles.isPending
            }
          >
            {mutation.isPending ? 'Saving…' : 'Save assignment'}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}
