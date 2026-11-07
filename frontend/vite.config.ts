import { fileURLToPath, URL } from 'node:url'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vitest/config'
import { loadEnv } from 'vite'

export default defineConfig(({ mode }) => {
  const apiTarget =
    process.env.API_PROXY_TARGET ??
    loadEnv(mode, process.cwd(), 'API_PROXY_').API_PROXY_TARGET ??
    'http://127.0.0.1:5080'
  return {
    plugins: [react(), tailwindcss()],
    resolve: {
      alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
    },
    server: {
      port: 5173,
      strictPort: true,
      proxy: {
        '/health': apiTarget,
        '/api': apiTarget,
        '/openapi': apiTarget,
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test-setup.ts'],
      include: ['src/**/*.test.{ts,tsx}'],
      coverage: {
        provider: 'v8',
        reporter: ['text-summary', 'html', 'lcov', 'json-summary'],
        include: ['src/**/*.{ts,tsx}'],
        exclude: [
          'src/**/*.test.{ts,tsx}',
          'src/test/**',
          'src/test-setup.ts',
          'src/main.tsx',
          'src/types/api.generated.ts',
        ],
      },
      clearMocks: true,
      restoreMocks: true,
    },
  }
})
