import { ResourcePage } from '@/components/shared/ResourcePage'
import { ResourceForm } from '@/components/shared/ResourceForm'
import { Field } from '@/components/shared/Field'
import { AvailabilityBadge } from '@/components/shared/StatusBadge'
import { vehicleSchema } from '@/types/api'
import { kilograms } from '@/lib/dates'
import { z } from 'zod'
const validation = z.object({
  plateNumber: z.string().trim().min(1, 'Plate number is required.').max(20),
  type: z.enum(['Van', 'Truck']),
  maxPayloadKg: z
    .number({ error: 'Enter a valid capacity.' })
    .positive('Capacity must be greater than zero.')
    .max(9999999999.99)
    .refine(
      (n) => Math.abs(n * 100 - Math.round(n * 100)) < 0.0001,
      'Use at most two decimal places.',
    ),
  status: z.enum(['Available', 'Maintenance', 'Inactive']),
})
export function VehiclesPage() {
  return (
    <ResourcePage
      resource="vehicles"
      title="Vehicles"
      singular="vehicle"
      description="Keep your fleet capacity and operational availability in view."
      schema={vehicleSchema}
      editable
      columns={[
        {
          label: 'Registration',
          render: (v) => (
            <strong className="plate-number">{v.plateNumber}</strong>
          ),
        },
        { label: 'Type', render: (v) => v.type },
        { label: 'Maximum payload', render: (v) => kilograms(v.maxPayloadKg) },
        {
          label: 'Availability',
          render: (v) => <AvailabilityBadge value={v.status} />,
        },
      ]}
      renderForm={(item, close, setBusy) => (
        <ResourceForm
          resource="vehicles"
          schema={vehicleSchema}
          validation={validation}
          id={item?.id}
          defaults={
            item
              ? {
                  plateNumber: item.plateNumber,
                  type: item.type,
                  maxPayloadKg: item.maxPayloadKg,
                  status: item.status,
                }
              : {
                  plateNumber: '',
                  type: 'Truck',
                  maxPayloadKg: 10000,
                  status: 'Available',
                }
          }
          close={close}
          setBusy={setBusy}
          fields={['plateNumber', 'type', 'maxPayloadKg', 'status']}
          codes={{
            duplicate_value: ['plateNumber'],
            invalid_weight: ['maxPayloadKg'],
          }}
        >
          {({ register, formState: { errors } }) => (
            <>
              <Field
                label="Registration plate"
                error={errors.plateNumber?.message}
              >
                <input
                  className="control uppercase"
                  maxLength={20}
                  {...register('plateNumber')}
                />
              </Field>
              <Field label="Vehicle type" error={errors.type?.message}>
                <select className="control" {...register('type')}>
                  <option>Truck</option>
                  <option>Van</option>
                </select>
              </Field>
              <Field
                label="Maximum payload (kg)"
                error={errors.maxPayloadKg?.message}
              >
                <input
                  className="control"
                  type="number"
                  min="0.01"
                  step="0.01"
                  {...register('maxPayloadKg', { valueAsNumber: true })}
                />
              </Field>
              <Field label="Availability" error={errors.status?.message}>
                <select className="control" {...register('status')}>
                  <option>Available</option>
                  <option>Maintenance</option>
                  <option>Inactive</option>
                </select>
              </Field>
              <div className="info-note">
                Capacity reductions and availability changes are checked against
                existing assignments by the server.
              </div>
            </>
          )}
        </ResourceForm>
      )}
    />
  )
}
