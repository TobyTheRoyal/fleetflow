import { useEffect, useState, type ReactNode } from 'react'
import {
  useForm,
  type FieldValues,
  type DefaultValues,
  type UseFormReturn,
  type FieldPath,
} from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { ErrorNotice } from './Feedback'
import { applyServerErrors } from '@/lib/form-errors'
import { useResourceWrite } from '@/lib/resource-write'
export function ResourceForm<T, Values extends FieldValues>({
  resource,
  schema,
  validation,
  defaults,
  id,
  close,
  setBusy,
  children,
  fields,
  codes,
}: {
  resource: string
  schema: z.ZodType<T>
  validation: z.ZodType<Values, Values>
  defaults: DefaultValues<Values>
  id?: string
  close: () => void
  setBusy: (value: boolean) => void
  children: (form: UseFormReturn<Values>) => ReactNode
  fields: FieldPath<Values>[]
  codes?: Record<string, FieldPath<Values>[]>
}) {
  const form = useForm<Values>({
    resolver: zodResolver(validation),
    defaultValues: defaults,
  })
  const mutation = useResourceWrite<T, Values>(resource, schema, id)
  const [failure, setFailure] = useState<unknown>(null)
  const pending = form.formState.isSubmitting
  useEffect(() => {
    setBusy(pending)
    return () => setBusy(false)
  }, [pending, setBusy])
  return (
    <form
      className="dialog-body space-y-5"
      noValidate
      onSubmit={form.handleSubmit(async (values) => {
        if (mutation.isPending) return
        setFailure(null)
        try {
          await mutation.mutateAsync(values)
          toast.success(id ? 'Changes saved' : 'Record created')
          close()
        } catch (error) {
          setFailure(error)
          applyServerErrors(error, form.setError, fields, codes)
        }
      })}
    >
      {failure ? <ErrorNotice error={failure} /> : null}
      {children(form)}
      <div className="dialog-actions">
        <Button variant="outline" disabled={pending} onClick={close}>
          Cancel
        </Button>
        <Button type="submit" disabled={pending}>
          {pending ? 'Saving…' : id ? 'Save changes' : 'Create record'}
        </Button>
      </div>
    </form>
  )
}
