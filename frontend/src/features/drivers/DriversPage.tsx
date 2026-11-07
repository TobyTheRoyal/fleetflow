import { ResourcePage } from '@/components/shared/ResourcePage'
import { ResourceForm } from '@/components/shared/ResourceForm'
import { Field } from '@/components/shared/Field'
import { AvailabilityBadge } from '@/components/shared/StatusBadge'
import { driverSchema } from '@/types/api'
import { z } from 'zod'
const validation = z.object({
  employeeNumber: z
    .string()
    .trim()
    .min(1, 'Employee number is required.')
    .max(40),
  firstName: z.string().trim().min(1, 'First name is required.').max(100),
  lastName: z.string().trim().min(1, 'Last name is required.').max(100),
  active: z.boolean(),
  licenseCategory: z.string().trim().max(30),
})
export function DriversPage() {
  return (
    <ResourcePage
      resource="drivers"
      title="Drivers"
      singular="driver"
      description="Manage your driving team and dispatch eligibility."
      schema={driverSchema}
      editable
      columns={[
        {
          label: 'Driver',
          render: (d) => (
            <div className="person-cell">
              <span className="person-initials">
                {d.firstName[0]}
                {d.lastName[0]}
              </span>
              <strong>
                {d.firstName} {d.lastName}
              </strong>
            </div>
          ),
        },
        {
          label: 'Employee number',
          render: (d) => <span className="mono">{d.employeeNumber}</span>,
        },
        { label: 'Licence category', render: (d) => d.licenseCategory || '—' },
        {
          label: 'Status',
          render: (d) => (
            <AvailabilityBadge value={d.active ? 'Active' : 'Inactive'} />
          ),
        },
      ]}
      renderForm={(item, close, setBusy) => (
        <ResourceForm
          resource="drivers"
          schema={driverSchema}
          validation={validation}
          id={item?.id}
          defaults={
            item
              ? {
                  employeeNumber: item.employeeNumber,
                  firstName: item.firstName,
                  lastName: item.lastName,
                  active: item.active,
                  licenseCategory: item.licenseCategory ?? '',
                }
              : {
                  employeeNumber: '',
                  firstName: '',
                  lastName: '',
                  active: true,
                  licenseCategory: '',
                }
          }
          close={close}
          setBusy={setBusy}
          fields={[
            'employeeNumber',
            'firstName',
            'lastName',
            'active',
            'licenseCategory',
          ]}
          codes={{ duplicate_value: ['employeeNumber'] }}
        >
          {({ register, formState: { errors } }) => (
            <>
              <Field
                label="Employee number"
                error={errors.employeeNumber?.message}
              >
                <input
                  className="control"
                  maxLength={40}
                  {...register('employeeNumber')}
                />
              </Field>
              <div className="grid grid-cols-2 gap-4">
                <Field label="First name" error={errors.firstName?.message}>
                  <input
                    className="control"
                    maxLength={100}
                    {...register('firstName')}
                  />
                </Field>
                <Field label="Last name" error={errors.lastName?.message}>
                  <input
                    className="control"
                    maxLength={100}
                    {...register('lastName')}
                  />
                </Field>
              </div>
              <Field
                label="Licence category (optional)"
                error={errors.licenseCategory?.message}
              >
                <input
                  className="control"
                  maxLength={30}
                  {...register('licenseCategory')}
                />
              </Field>
              <label className="checkbox-field">
                <input type="checkbox" {...register('active')} />
                <span>
                  <strong>Active driver</strong>
                  <span>
                    Eligible for new assignments. Existing reservations may
                    prevent deactivation.
                  </span>
                </span>
              </label>
            </>
          )}
        </ResourceForm>
      )}
    />
  )
}
