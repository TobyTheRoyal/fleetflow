import { useState } from 'react'
import { toast } from 'sonner'
import { useQueryClient } from '@tanstack/react-query'
import { Dialog } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/shared/Field'
import { ErrorNotice, StaleNotice } from '@/components/shared/Feedback'
import { ApiError } from '@/lib/api'
import { useShipmentWrite } from '@/lib/queries'
import type { Shipment, ShipmentStatus } from '@/types/api'
export function StatusDialog({
  shipment,
  target,
  close,
}: {
  shipment: Shipment
  target: ShipmentStatus
  close: () => void
}) {
  const mutation = useShipmentWrite(shipment.id)
  const client = useQueryClient()
  const [reason, setReason] = useState('')
  const [failure, setFailure] = useState<unknown>(null)
  const [refreshing, setRefreshing] = useState(false)
  const stale = failure instanceof ApiError && failure.code === 'stale_version'
  const label =
    target === 'Planned'
      ? 'Plan shipment'
      : target === 'InTransit'
        ? 'Start transit'
        : target === 'Delivered'
          ? 'Mark delivered'
          : 'Cancel shipment'
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
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) close()
      }}
      title={label}
      description={`${shipment.shipmentNumber} · current version ${shipment.version}`}
      busy={mutation.isPending}
    >
      <form
        className="dialog-body space-y-5"
        onSubmit={async (event) => {
          event.preventDefault()
          if (mutation.isPending || stale) return
          setFailure(null)
          try {
            await mutation.mutateAsync({
              action: 'status',
              body: {
                version: shipment.version,
                status: target,
                reason: reason.trim() || null,
              },
            })
            toast.success(
              target === 'Cancelled'
                ? 'Shipment cancelled'
                : `Shipment is now ${target === 'InTransit' ? 'in transit' : target.toLowerCase()}`,
            )
            close()
          } catch (error) {
            setFailure(error)
          }
        }}
      >
        {stale ? (
          <StaleNotice onReload={() => void reload()} busy={refreshing} />
        ) : failure ? (
          <ErrorNotice error={failure} />
        ) : null}
        <p className="text-sm leading-6 text-slate-600">
          {target === 'Cancelled'
            ? 'This closes the shipment and releases its reservation. Cancellation is final and cannot be undone.'
            : target === 'Delivered'
              ? 'Confirm that this shipment has been delivered. The delivery time will be recorded and the shipment will be closed.'
              : target === 'InTransit'
                ? 'Confirm pickup and departure. The actual pickup time will be recorded. Assignment and shipment details will then be locked.'
                : 'The driver and vehicle will be checked again before this transport is planned.'}
        </p>
        <Field
          label="Reason (optional)"
          hint="Recorded in the shipment timeline · maximum 1,000 characters."
        >
          <textarea
            className="control textarea"
            rows={3}
            maxLength={1000}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
        </Field>
        <div className="dialog-actions">
          <Button
            variant="outline"
            onClick={close}
            disabled={mutation.isPending}
          >
            Go back
          </Button>
          <Button
            variant={target === 'Cancelled' ? 'danger' : 'default'}
            type="submit"
            disabled={mutation.isPending || stale || refreshing}
          >
            {mutation.isPending ? 'Saving…' : label}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}
