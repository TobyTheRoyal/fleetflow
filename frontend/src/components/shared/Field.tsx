import { useId } from 'react'
import type { ReactElement, ReactNode } from 'react'
import { cloneElement } from 'react'
export function Field({
  label,
  error,
  hint,
  children,
  className = '',
}: {
  label: string
  error?: string
  hint?: string
  children: ReactElement<{
    id?: string
    'aria-invalid'?: boolean
    'aria-describedby'?: string
  }>
  className?: string
}) {
  const id = useId()
  return (
    <div className={`field ${className}`}>
      <label htmlFor={id}>{label}</label>
      {cloneElement(children, {
        id,
        'aria-invalid': !!error,
        'aria-describedby': error
          ? `${id}-error`
          : hint
            ? `${id}-hint`
            : undefined,
      })}
      {error ? (
        <p id={`${id}-error`} className="field-error">
          {error}
        </p>
      ) : hint ? (
        <p id={`${id}-hint`} className="field-hint">
          {hint}
        </p>
      ) : null}
    </div>
  )
}
export function FormSection({
  title,
  description,
  children,
}: {
  title: string
  description?: string
  children: ReactNode
}) {
  return (
    <section className="form-section">
      <div className="mb-5">
        <h2 className="font-semibold">{title}</h2>
        {description && (
          <p className="mt-1 text-sm text-slate-500">{description}</p>
        )}
      </div>
      <div className="form-grid">{children}</div>
    </section>
  )
}
