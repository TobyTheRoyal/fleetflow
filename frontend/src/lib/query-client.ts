import { QueryClient } from '@tanstack/react-query'
import { ApiError } from './api'
export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 15_000,
        retry: (count, error) =>
          count < 1 &&
          (!(error instanceof ApiError) ||
            error.status === 0 ||
            error.status >= 500),
      },
      mutations: { retry: false },
    },
  })
}
