import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { toast } from 'sonner'
import { Dialog } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/shared/Field'
import { ErrorNotice } from '@/components/shared/Feedback'
import { request, resetCsrf } from '@/lib/api'
const schema = z.object({
  currentPassword: z.string().min(1).max(128),
  newPassword: z.string().min(12).max(128),
})
export function PasswordDialog({ close }: { close: () => void }) {
  const [error, setError] = useState<unknown>(null)
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema) })
  return (
    <Dialog
      open
      busy={isSubmitting}
      onOpenChange={(open) => {
        if (!open) close()
      }}
      title="Change password"
      description="Your current password is required. Other sessions will be signed out."
    >
      <form
        className="dialog-body space-y-5"
        noValidate
        onSubmit={handleSubmit(async (values) => {
          setError(null)
          try {
            await request(
              '/api/v1/auth/password',
              z.object({ success: z.boolean() }),
              { method: 'POST', body: values },
            )
            resetCsrf()
            reset()
            toast.success('Password changed. Other sessions have been revoked.')
            close()
          } catch (failure) {
            setError(failure)
            reset()
          }
        })}
      >
        {error ? <ErrorNotice error={error} /> : null}
        <Field label="Current password" error={errors.currentPassword?.message}>
          <input
            className="control"
            type="password"
            autoComplete="current-password"
            {...register('currentPassword')}
          />
        </Field>
        <Field
          label="New password"
          error={errors.newPassword?.message}
          hint="12–128 characters, at least 6 distinct characters, uppercase, lowercase, digit and symbol."
        >
          <input
            className="control"
            type="password"
            autoComplete="new-password"
            {...register('newPassword')}
          />
        </Field>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Saving…' : 'Update password'}
        </Button>
      </form>
    </Dialog>
  )
}
