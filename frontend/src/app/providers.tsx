import { QueryClientProvider } from '@tanstack/react-query'
import type { PropsWithChildren } from 'react'
import { BrowserRouter } from 'react-router-dom'
import { Toaster } from 'sonner'
import { SessionProvider } from '@/features/auth/session'
import { createQueryClient } from '@/lib/query-client'
const queryClient = createQueryClient()
export function AppProviders({ children }: PropsWithChildren) {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <SessionProvider>{children}</SessionProvider>
      </BrowserRouter>
      <Toaster richColors closeButton position="bottom-right" />
    </QueryClientProvider>
  )
}
