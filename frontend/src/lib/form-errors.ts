import type { FieldPath, FieldValues, UseFormSetError } from 'react-hook-form'
import { ApiError } from './api'
export function applyServerErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fields: FieldPath<T>[],
  codes: Record<string, FieldPath<T>[]> = {},
  aliases: Record<string, FieldPath<T>> = {},
) {
  if (!(error instanceof ApiError)) return
  const allowed = new Set<string>(fields)
  Object.entries(error.fields).forEach(([key, messages]) => {
    const normalized = key
      .replace(/^\$\./, '')
      .split('.')
      .map((part) => part[0]?.toLowerCase() + part.slice(1))
      .join('.')
    const path = aliases[normalized] ?? normalized
    if (allowed.has(path))
      setError(path as FieldPath<T>, {
        type: 'server',
        message: messages.join(' '),
      })
  })
  codes[error.code]?.forEach((path) =>
    setError(path, { type: 'server', message: error.message }),
  )
}
