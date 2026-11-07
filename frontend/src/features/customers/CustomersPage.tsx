import { ResourcePage } from '@/components/shared/ResourcePage'
import { ResourceForm } from '@/components/shared/ResourceForm'
import { Field } from '@/components/shared/Field'
import { customerSchema } from '@/types/api'
import { z } from 'zod'
const validation = z.object({
  name: z.string().trim().min(1, 'Company name is required.').max(200),
  contactName: z.string().trim().min(1, 'Contact name is required.').max(150),
  email: z.email('Enter a valid email address.').max(254),
  phone: z.string().trim().max(40),
})
export function CustomersPage() {
  return (
    <ResourcePage
      resource="customers"
      title="Customers"
      singular="customer"
      description="The companies and contacts behind every shipment."
      schema={customerSchema}
      columns={[
        { label: 'Company', render: (c) => <strong>{c.name}</strong> },
        { label: 'Contact', render: (c) => c.contactName },
        {
          label: 'Email',
          render: (c) => (
            <a className="table-link" href={`mailto:${c.email}`}>
              {c.email}
            </a>
          ),
        },
        { label: 'Phone', render: (c) => c.phone || '—' },
      ]}
      renderForm={(_, close, setBusy) => (
        <ResourceForm
          resource="customers"
          schema={customerSchema}
          validation={validation}
          defaults={{ name: '', contactName: '', email: '', phone: '' }}
          close={close}
          setBusy={setBusy}
          fields={['name', 'contactName', 'email', 'phone']}
          codes={{ invalid_email: ['email'] }}
        >
          {({ register, formState: { errors } }) => (
            <>
              <Field label="Company name" error={errors.name?.message}>
                <input
                  className="control"
                  maxLength={200}
                  autoComplete="organization"
                  {...register('name')}
                />
              </Field>
              <Field label="Contact name" error={errors.contactName?.message}>
                <input
                  className="control"
                  maxLength={150}
                  autoComplete="name"
                  {...register('contactName')}
                />
              </Field>
              <Field label="Email" error={errors.email?.message}>
                <input
                  className="control"
                  type="email"
                  maxLength={254}
                  autoComplete="email"
                  {...register('email')}
                />
              </Field>
              <Field label="Phone (optional)" error={errors.phone?.message}>
                <input
                  className="control"
                  type="tel"
                  maxLength={40}
                  autoComplete="tel"
                  {...register('phone')}
                />
              </Field>
            </>
          )}
        </ResourceForm>
      )}
    />
  )
}
