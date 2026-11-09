import { defineConfig, devices } from '@playwright/test'
export default defineConfig({
  testDir: './tests/e2e',
  testMatch: '**/*.spec.ts',
  globalSetup: './tests/e2e/infra/stack.ts',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  reporter: [
    ['list'],
    ['html', { open: 'never' }],
    ['junit', { outputFile: 'test-results/results.xml' }],
  ],
  use: {
    baseURL: 'http://127.0.0.1:5174',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } },
  ],
  webServer: [
    {
      command: 'npm run dev -- --port 5174',
      url: 'http://127.0.0.1:5174',
      reuseExistingServer: false,
      env: {
        API_PROXY_TARGET: 'http://127.0.0.1:5180',
        VITE_API_BASE_URL: '',
      },
    },
    {
      command: 'npm run dev -- --port 5175',
      url: 'http://127.0.0.1:5175',
      reuseExistingServer: false,
      env: { API_PROXY_TARGET: 'http://127.0.0.1:5181', VITE_API_BASE_URL: '' },
    },
  ],
})
