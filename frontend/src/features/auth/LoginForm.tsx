import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Navigate } from 'react-router-dom'
import { Route } from 'lucide-react'
import { Field } from '@/components/shared/Field'
import { ErrorNotice } from '@/components/shared/Feedback'
import { Button } from '@/components/ui/button'
import { request } from '@/lib/api'
import { useSession, sessionSchema } from './context'
const schema = z.object({
  email: z.email('Enter a valid email address.').max(254),
  password: z.string().min(1, 'Enter your password.').max(128),
})
export function LoginForm({
  reauthenticate = false,
}: {
  reauthenticate?: boolean
}) {
  const session = useSession()
  const [error, setError] = useState<unknown>(null)
  const {
    register,
    handleSubmit,
    resetField,
    formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      email: reauthenticate ? (session.user?.email ?? '') : '',
      password: '',
    },
  })
  return (
    <form
      noValidate
      className="space-y-5"
      onSubmit={handleSubmit(async (values) => {
        setError(null)
        try {
          session.signedIn(
            await request('/api/v1/auth/login', sessionSchema, {
              method: 'POST',
              body: values,
            }),
          )
          resetField('password')
        } catch (failure) {
          setError(failure)
          resetField('password')
        }
      })}
    >
      {error ? <ErrorNotice error={error} /> : null}
      <Field label="Email" error={errors.email?.message}>
        <input
          className="control"
          type="email"
          autoComplete="username"
          maxLength={254}
          {...register('email')}
        />
      </Field>
      <Field label="Password" error={errors.password?.message}>
        <input
          className="control"
          type="password"
          autoComplete="current-password"
          maxLength={128}
          {...register('password')}
        />
      </Field>
      <Button
        className="w-full"
        type="submit"
        disabled={isSubmitting || !session.ready}
      >
        {isSubmitting
          ? 'Signing in…'
          : !session.ready
            ? 'Checking session…'
            : 'Sign in'}
      </Button>
      <p className="text-sm text-slate-600">
        Accounts are managed by your administrator. Public registration is
        unavailable.
      </p>
    </form>
  )
}
export function LoginPage() {
  const { user, ready, portfolioMode } = useSession()
  if (portfolioMode) return <Navigate to="/demo" replace />
  if (ready && user) return <Navigate to="/" replace />
  return (
    <main className="login-page">
      <section className="login-card">
        <div className="flex items-center gap-3 text-blue-700 mb-7">
          <Route size={30} />
          <strong className="text-2xl">FleetFlow</strong>
        </div>
        <p className="eyebrow">LOGISTICS WORKSPACE</p>
        <h1 className="text-3xl font-semibold mt-3">Welcome back</h1>
        <p className="text-slate-600 mt-3 mb-7">
          Sign in to your operations workspace.
        </p>
        <LoginForm />
      </section>
    </main>
  )
}
