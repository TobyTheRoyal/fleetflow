import { useState } from 'react'
import { useForm } from 'react-hook-form'
import type { FieldPath } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import {
  ArrowLeft,
  Check,
  LoaderCircle,
  MapPin,
  Package,
  CalendarDays,
} from 'lucide-react'
import { PageHeader } from '@/components/shared/PageHeader'
import { Button } from '@/components/ui/button'
import { Field, FormSection } from '@/components/shared/Field'
import { ErrorNotice, StaleNotice } from '@/components/shared/Feedback'
import { useCustomerOptions, useShipmentWrite } from '@/lib/queries'
import { ApiError, request } from '@/lib/api'
import { applyServerErrors } from '@/lib/form-errors'
import { TIME_ZONE, formatDate, kilograms } from '@/lib/dates'
import { shipmentSchema } from '@/types/api'
import type { Shipment } from '@/types/api'
import { defaults, payload, shipmentFormSchema } from './validation'
import type { ShipmentFormValues } from './validation'
export function ShipmentForm({ initial }: { initial?: Shipment }) {
  const [baseline, setBaseline] = useState(initial)
  const [error, setError] = useState<unknown>(null)
  const [reloading, setReloading] = useState(false)
  const navigate = useNavigate()
  const client = useQueryClient()
  const mutation = useShipmentWrite(initial?.id)
  const customers = useCustomerOptions()
  const options = customers.data?.pages.flatMap((p) => p.items) ?? []
  const form = useForm<ShipmentFormValues>({
    resolver: zodResolver(shipmentFormSchema(baseline)),
    defaultValues: defaults(baseline),
    mode: 'onBlur',
  })
  const {
    register,
    formState: { errors, isSubmitting },
  } = form
  const planned = baseline?.status === 'Planned'
  const stale = error instanceof ApiError && error.code === 'stale_version'
  const busy = isSubmitting || mutation.isPending
  const reload = async () => {
    if (!baseline) return
    setReloading(true)
    try {
      const fresh = await client.fetchQuery({
        queryKey: ['shipments', baseline.id],
        queryFn: ({ signal }) =>
          request(`/api/v1/shipments/${baseline.id}`, shipmentSchema, {
            signal,
          }),
        staleTime: 0,
      })
      setBaseline(fresh)
      form.reset(defaults(fresh))
      setError(null)
    } catch (failure) {
      toast.error(
        failure instanceof Error
          ? failure.message
          : 'Could not reload the shipment. Try again.',
      )
    } finally {
      setReloading(false)
    }
  }
  const submit = form.handleSubmit(async (values) => {
    if (mutation.isPending || stale) return
    setError(null)
    try {
      const body = payload(values, baseline)
      const saved = baseline
        ? await mutation.mutateAsync({
            action: 'update',
            body: { ...body, version: baseline.version },
          })
        : await mutation.mutateAsync({ action: 'create', body })
      toast.success(baseline ? 'Shipment updated' : 'Shipment created')
      navigate(`/shipments/${saved.id}`)
    } catch (failure) {
      setError(failure)
      applyServerErrors(
        failure,
        form.setError,
        [
          'customerId',
          'weightKg',
          'palletCount',
          'pickup',
          'deadline',
          'origin.countryCode',
          'destination.countryCode',
          'origin.street',
          'destination.street',
          'origin.city',
          'destination.city',
          'origin.postcode',
          'destination.postcode',
          'cargoDescription',
        ],
        {
          invalid_weight: ['weightKg'],
          vehicle_overloaded: ['weightKg'],
          invalid_schedule: ['deadline'],
          invalid_timestamp: ['pickup', 'deadline'],
          invalid_pallet_count: ['palletCount'],
          invalid_country: ['origin.countryCode', 'destination.countryCode'],
        },
        { pickupWindowStartUtc: 'pickup', deliveryDeadlineUtc: 'deadline' },
      )
    }
  })
  const locked = baseline && !['Draft', 'Planned'].includes(baseline.status)
  return (
    <>
      <Link
        to={baseline ? `/shipments/${baseline.id}` : '/shipments'}
        className="back-link"
      >
        <ArrowLeft size={15} />
        {baseline ? 'Back to shipment' : 'Back to shipments'}
      </Link>
      <PageHeader
        eyebrow="TRANSPORT ORDER"
        title={baseline ? `Edit ${baseline.shipmentNumber}` : 'Create shipment'}
        description={
          planned
            ? 'Only the cargo description can change after planning.'
            : 'Add the customer, route, cargo and schedule for your transport order.'
        }
      />
      {locked ? (
        <div className="panel p-6">
          <p>
            This shipment is {baseline.status}. Its details can no longer be
            edited.
          </p>
          <Button className="mt-4" asChild>
            <Link to={`/shipments/${baseline.id}`}>Return to shipment</Link>
          </Button>
        </div>
      ) : (
        <form
          noValidate
          onSubmit={submit}
          className="shipment-form form-layout"
        >
          <div className="form-main">
            {stale ? (
              <StaleNotice onReload={() => void reload()} busy={reloading} />
            ) : error ? (
              <ErrorNotice error={error} />
            ) : null}
            {planned ? (
              <section className="panel p-6">
                <h2 className="font-semibold">Planned shipment details</h2>
                <p className="mt-2 text-sm text-slate-500">
                  {baseline.customerName ?? 'Selected customer'} ·{' '}
                  {baseline.origin.city} → {baseline.destination.city} ·{' '}
                  {kilograms(baseline.weightKg)}
                </p>
                <p className="mt-2 text-sm text-slate-500">
                  {formatDate(baseline.pickupWindowStartUtc)} →{' '}
                  {formatDate(baseline.deliveryDeadlineUtc)} ({TIME_ZONE})
                </p>
                <p className="mt-4 text-xs text-slate-500">
                  Route, customer, cargo weight and schedule are locked after
                  planning.
                </p>
              </section>
            ) : (
              <>
                <FormSection
                  title="Customer"
                  description="Choose the company requesting this transport."
                >
                  <div className="col-span-full">
                    <Field label="Customer" error={errors.customerId?.message}>
                      <select className="control" {...register('customerId')}>
                        <option value="">
                          {customers.isPending
                            ? 'Loading customers…'
                            : 'Select a customer'}
                        </option>
                        {baseline &&
                          !options.some(
                            (c) => c.id === baseline.customerId,
                          ) && (
                            <option value={baseline.customerId}>
                              {baseline.customerName ?? baseline.customerId}
                            </option>
                          )}
                        {options.map((c) => (
                          <option key={c.id} value={c.id}>
                            {c.name}
                          </option>
                        ))}
                      </select>
                    </Field>
                    {customers.isError && (
                      <ErrorNotice
                        error={customers.error}
                        retry={() => void customers.refetch()}
                      />
                    )}
                    {customers.hasNextPage && (
                      <button
                        type="button"
                        className="text-button mt-2"
                        onClick={() => void customers.fetchNextPage()}
                        disabled={customers.isFetchingNextPage}
                      >
                        Load more customers
                      </button>
                    )}
                    <Link
                      to="/customers"
                      className="field-hint inline-block mt-2 underline"
                    >
                      Manage customers
                    </Link>
                  </div>
                </FormSection>
                <FormSection
                  title="Route"
                  description="Enter complete pickup and delivery addresses."
                >
                  {(['origin', 'destination'] as const).map((side) => (
                    <div key={side} className="address-group">
                      <h3 className="flex items-center gap-2">
                        <MapPin size={16} />
                        {side === 'origin'
                          ? 'Pickup address'
                          : 'Delivery address'}
                      </h3>
                      {(
                        [
                          { key: 'street', label: 'Street and number' },
                          { key: 'city', label: 'City' },
                          { key: 'postcode', label: 'Postcode' },
                          { key: 'countryCode', label: 'Country code' },
                        ] as const
                      ).map(({ key, label }) => (
                        <Field
                          key={key}
                          label={`${side === 'origin' ? 'Origin' : 'Destination'} ${label.toLowerCase()}`}
                          error={errors[side]?.[key]?.message}
                        >
                          <input
                            className="control"
                            autoComplete="off"
                            maxLength={
                              key === 'street'
                                ? 200
                                : key === 'city'
                                  ? 100
                                  : key === 'countryCode'
                                    ? 2
                                    : 20
                            }
                            {...register(
                              `${side}.${key}` as FieldPath<ShipmentFormValues>,
                            )}
                          />
                        </Field>
                      ))}
                    </div>
                  ))}
                </FormSection>
              </>
            )}
            <FormSection
              title="Cargo"
              description="Describe the load and its handling requirements."
            >
              {!planned && (
                <>
                  <Field label="Weight (kg)" error={errors.weightKg?.message}>
                    <input
                      className="control"
                      type="number"
                      min="0.01"
                      max="9999999999.99"
                      step="0.01"
                      {...register('weightKg', { valueAsNumber: true })}
                    />
                  </Field>
                  <Field
                    label="Pallet count"
                    error={errors.palletCount?.message}
                  >
                    <input
                      className="control"
                      type="number"
                      min="0"
                      step="1"
                      {...register('palletCount', { valueAsNumber: true })}
                    />
                  </Field>
                </>
              )}
              <Field
                className="col-span-full"
                label="Cargo description (optional)"
                error={errors.cargoDescription?.message}
                hint="Maximum 1,000 characters."
              >
                <textarea
                  className="control textarea"
                  rows={3}
                  maxLength={1000}
                  placeholder="Describe the goods and any handling notes…"
                  {...register('cargoDescription')}
                />
              </Field>
            </FormSection>
            {!planned && (
              <FormSection
                title="Schedule"
                description={`Enter local times in ${TIME_ZONE}. They are stored as UTC.`}
              >
                <Field
                  label="Pickup time"
                  error={errors.pickup?.message}
                  hint="Europe/Vienna (CET/CEST)"
                >
                  <input
                    className="control"
                    type="datetime-local"
                    {...register('pickup')}
                  />
                </Field>
                <Field
                  label="Delivery deadline"
                  error={errors.deadline?.message}
                  hint="Must be later than pickup · Europe/Vienna"
                >
                  <input
                    className="control"
                    type="datetime-local"
                    {...register('deadline')}
                  />
                </Field>
              </FormSection>
            )}
            <div className="form-actions">
              <Button variant="outline" asChild>
                <Link
                  to={baseline ? `/shipments/${baseline.id}` : '/shipments'}
                >
                  Cancel
                </Link>
              </Button>
              <Button type="submit" disabled={busy || stale || reloading}>
                {busy ? (
                  <LoaderCircle size={16} className="animate-spin" />
                ) : (
                  <Check size={16} />
                )}
                {busy
                  ? 'Saving…'
                  : baseline
                    ? 'Save changes'
                    : 'Create shipment'}
              </Button>
            </div>
          </div>
          <aside className="form-aside">
            <div className="panel p-6">
              <span className="form-aside-icon">
                <Package size={24} />
              </span>
              <h2 className="mt-5 font-semibold">
                {baseline ? 'Keep your team in sync' : 'Ready for dispatch'}
              </h2>
              <p className="mt-3 text-sm leading-6 text-slate-500">
                {baseline
                  ? 'Changes are saved with the shipment version you opened. If another operator updates it, you’ll be asked to reload first.'
                  : 'New shipments start as Draft. Once saved, assign a driver and vehicle, then plan the transport.'}
              </p>
              <div className="form-note">
                <CalendarDays size={16} />
                <span>
                  All schedule inputs use Europe/Vienna. Delivery times are
                  displayed in the same zone.
                </span>
              </div>
              {baseline && (
                <p className="mt-4 text-xs text-slate-400">
                  Editing version {baseline.version}
                </p>
              )}
            </div>
          </aside>
        </form>
      )}
    </>
  )
}
