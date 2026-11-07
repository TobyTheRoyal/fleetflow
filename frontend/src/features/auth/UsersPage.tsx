import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { request } from '@/lib/api'
import { pageSchema } from '@/types/api'
import { usePaging } from '@/lib/paging'
import { PageHeader } from '@/components/shared/PageHeader'
import { Pagination } from '@/components/shared/Pagination'
import { ErrorNotice, Skeleton } from '@/components/shared/Feedback'
import { Field } from '@/components/shared/Field'
import { Dialog } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { sessionSchema, useSession, type SessionUser } from './context'
const inputSchema = z.object({
  email: z.email().max(254),
  displayName: z.string().trim().min(1).max(150),
  role: z.enum(['Admin', 'Dispatcher', 'Viewer']),
  password: z.string().min(12).max(128),
})
function CreateUserForm({
  done,
  setBusy,
}: {
  done: () => void
  setBusy: (value: boolean) => void
}) {
  const [error, setError] = useState<unknown>(null)
  const {
    register,
    handleSubmit,
    resetField,
    formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof inputSchema>>({
    resolver: zodResolver(inputSchema),
    defaultValues: { email: '', displayName: '', role: 'Viewer', password: '' },
  })
  return (
    <form
      className="dialog-body space-y-5"
      noValidate
      onSubmit={handleSubmit(async (values) => {
        setError(null)
        setBusy(true)
        try {
          await request('/api/v1/users', sessionSchema, {
            method: 'POST',
            body: values,
          })
          done()
        } catch (failure) {
          setError(failure)
          resetField('password')
        } finally {
          setBusy(false)
        }
      })}
    >
      {error ? <ErrorNotice error={error} /> : null}
      <Field label="Email" error={errors.email?.message}>
        <input className="control" type="email" {...register('email')} />
      </Field>
      <Field label="Display name" error={errors.displayName?.message}>
        <input className="control" {...register('displayName')} />
      </Field>
      <Field label="Role" error={errors.role?.message}>
        <select className="control" {...register('role')}>
          <option>Viewer</option>
          <option>Dispatcher</option>
          <option>Admin</option>
        </select>
      </Field>
      <Field
        label="Initial password"
        error={errors.password?.message}
        hint="12–128 characters, at least 6 distinct characters, with uppercase, lowercase, digit and symbol. Share securely with the user."
      >
        <input
          className="control"
          type="password"
          autoComplete="new-password"
          {...register('password')}
        />
      </Field>
      <Button type="submit" disabled={isSubmitting}>
        {isSubmitting ? 'Creating…' : 'Create user'}
      </Button>
    </form>
  )
}
export function UsersPage() {
  const { user: current } = useSession()
  const cache = useQueryClient()
  const { page, pageSize, change } = usePaging()
  const query = useQuery({
    queryKey: ['users', page, pageSize],
    queryFn: ({ signal }) =>
      request(
        `/api/v1/users?page=${page}&pageSize=${pageSize}`,
        pageSchema(sessionSchema),
        { signal },
      ),
  })
  const [create, setCreate] = useState(false)
  const [selected, setSelected] = useState<SessionUser | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const done = () => {
    setCreate(false)
    setSelected(null)
    void cache.invalidateQueries({ queryKey: ['users'] })
  }
  return (
    <>
      <PageHeader
        eyebrow="ADMINISTRATION"
        title="Users"
        description="Manage workspace access. Deactivation immediately revokes existing sessions."
        action={<Button onClick={() => setCreate(true)}>Add user</Button>}
      />
      {query.isError && (
        <ErrorNotice error={query.error} retry={() => void query.refetch()} />
      )}
      {query.isPending ? (
        <Skeleton />
      ) : (
        query.data && (
          <section className="panel">
            <div
              className="table-scroll"
              role="region"
              aria-label="Users table"
              tabIndex={0}
            >
              <table className="data-table">
                <thead>
                  <tr>
                    <th scope="col">User</th>
                    <th scope="col">Role</th>
                    <th scope="col">Status</th>
                    <th scope="col">Access</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.items.map((user) => (
                    <tr key={user.id}>
                      <td>
                        <strong>{user.displayName}</strong>
                        <br />
                        {user.email}
                      </td>
                      <td>{user.role}</td>
                      <td>{user.active ? 'Active' : 'Inactive'}</td>
                      <td>
                        <Button
                          variant="outline"
                          disabled={current?.id === user.id}
                          onClick={() => {
                            setError(null)
                            setSelected(user)
                          }}
                        >
                          {user.active ? 'Deactivate' : 'Activate'}
                          <span className="sr-only"> {user.email}</span>
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination
              {...query.data}
              busy={query.isFetching}
              onPage={(value) => change({ page: value })}
              onSize={(value) => change({ page: 1, pageSize: value })}
            />
          </section>
        )
      )}
      {create && (
        <Dialog
          open
          onOpenChange={setCreate}
          busy={busy}
          title="Add user"
          description="Only administrators can provision accounts."
        >
          <CreateUserForm done={done} setBusy={setBusy} />
        </Dialog>
      )}
      {selected && (
        <Dialog
          open
          busy={busy}
          onOpenChange={(open) => {
            if (!open) setSelected(null)
          }}
          title={`${selected.active ? 'Deactivate' : 'Activate'} user`}
          description={`${selected.email}: existing sessions will be revoked.`}
        >
          <div className="dialog-body space-y-4">
            {error ? <ErrorNotice error={error} /> : null}
            <Button
              disabled={busy}
              onClick={async () => {
                setBusy(true)
                try {
                  await request(
                    `/api/v1/users/${selected.id}/activation`,
                    sessionSchema,
                    { method: 'PUT', body: { active: !selected.active } },
                  )
                  done()
                } catch (failure) {
                  setError(failure)
                } finally {
                  setBusy(false)
                }
              }}
            >
              {busy ? 'Saving…' : 'Confirm access change'}
            </Button>
          </div>
        </Dialog>
      )}
    </>
  )
}
