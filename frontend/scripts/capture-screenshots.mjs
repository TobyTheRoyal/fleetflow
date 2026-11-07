import { spawn } from 'node:child_process'
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { createServer } from 'node:net'
import { chromium, expect } from '@playwright/test'
import setup from '../tests/e2e/infra/stack.ts'
process.chdir(fileURLToPath(new URL('..', import.meta.url)))
// Refuse another listener: a screenshot must never accidentally use someone's data.
await new Promise((accept, reject) => {
  const probe = createServer()
  probe.once('error', reject)
  probe.listen(5174, '127.0.0.1', () => probe.close(accept))
})
const cleanup = await setup()
let server, browser
try {
  server = spawn(
    process.execPath,
    ['node_modules/vite/bin/vite.js', '--host', '127.0.0.1', '--port', '5174'],
    {
      stdio: 'ignore',
      env: {
        ...process.env,
        API_PROXY_TARGET: 'http://127.0.0.1:5180',
        VITE_API_BASE_URL: '',
      },
    },
  )
  const deadline = Date.now() + 30000
  while (true) {
    if (server.exitCode !== null)
      throw new Error('Screenshot Vite server exited.')
    try {
      if (
        (
          await fetch('http://127.0.0.1:5174/', {
            signal: AbortSignal.timeout(2000),
          })
        ).ok
      )
        break
    } catch {
      /* startup */
    }
    if (Date.now() > deadline)
      throw new Error('Screenshot preview did not become ready.')
    await new Promise((resolve) => setTimeout(resolve, 200))
  }
  browser = await chromium.launch()
  const context = await browser.newContext({
    viewport: { width: 1440, height: 1040 },
    locale: 'en-GB',
    timezoneId: 'Europe/Vienna',
    reducedMotion: 'reduce',
  })
  const page = await context.newPage()
  const records = await (
    await page.request.get(
      'http://127.0.0.1:5180/api/v1/shipments?status=Planned&pageSize=1',
    )
  ).json()
  const destination = resolve('../docs/screenshots')
  mkdirSync(destination, { recursive: true })
  for (const [name, path, fullPage] of [
    ['dashboard', '/', false],
    ['shipments', '/shipments', false],
    ['shipment-detail', `/shipments/${records.items[0].id}`, true],
    ['vehicles', '/vehicles', false],
  ]) {
    await page.goto('http://127.0.0.1:5174' + path)
    await page.getByRole('heading', { level: 1 }).waitFor()
    await expect(page.getByLabel('Loading data')).toHaveCount(0)
    await page
      .getByTestId('connection-status')
      .filter({ hasText: 'API and PostgreSQL are connected.' })
      .waitFor({ state: 'attached' })
    if (name === 'shipment-detail')
      await page.getByRole('heading', { name: 'Shipment created' }).waitFor()
    await page.screenshot({
      path: resolve(destination, `${name}.png`),
      fullPage,
      animations: 'disabled',
    })
  }
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('http://127.0.0.1:5174/')
  await page.getByText('Recent shipments', { exact: true }).waitFor()
  await page.screenshot({
    path: resolve(destination, 'mobile-dashboard.png'),
    fullPage: false,
    animations: 'disabled',
  })
  console.log(
    'Saved five actual seeded-application screenshots to docs/screenshots/.',
  )
  if (process.argv.includes('--preview')) {
    console.log(
      'Preview ready at http://127.0.0.1:5174/ — press Enter to remove only this disposable stack.',
    )
    await new Promise((resolve) => {
      process.stdin.once('data', resolve)
      process.stdin.resume()
    })
    process.stdin.pause()
  }
} finally {
  await browser?.close()
  if (server && server.exitCode === null) {
    const stopped = new Promise((resolve) => server.once('exit', resolve))
    server.kill('SIGTERM')
    await stopped
  }
  await cleanup()
}
