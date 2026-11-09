import { QueryClientProvider } from '@tanstack/react-query'
import type { PropsWithChildren } from 'react'
import { BrowserRouter } from 'react-router-dom'
import { Toaster } from 'sonner'
import { PortfolioBootstrap } from '@/features/demo/PortfolioBootstrap'
import { SessionProvider } from '@/features/auth/session'
import { createQueryClient } from '@/lib/query-client'
const queryClient = createQueryClient()
export function AppProviders({ children }: PropsWithChildren) {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <PortfolioBootstrap>
          {(enabled) => (
            <SessionProvider portfolioMode={enabled}>
              {children}
            </SessionProvider>
          )}
        </PortfolioBootstrap>
      </BrowserRouter>
      <Toaster richColors closeButton position="bottom-right" />
    </QueryClientProvider>
  )
}
