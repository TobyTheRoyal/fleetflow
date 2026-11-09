// Explicit LOCAL browser rehearsal only. Never contacts a hosted deployment or inference.
import { chromium, firefox, webkit, expect } from '@playwright/test'
import { fileURLToPath } from 'node:url'
if (!process.argv.includes('--accept-local-test-certificate'))
  throw new Error(
    'This checks only https://localhost:5443. Explicitly pass --accept-local-test-certificate for its self-signed rehearsal certificate; verify CA/hostname separately with the Python smoke.',
  )
for (const [name, engine] of Object.entries({ chromium, firefox, webkit })) {
  const browser = await engine.launch()
  try {
    const context = await browser.newContext({
      ignoreHTTPSErrors: true,
      viewport: { width: 1440, height: 1050 },
    })
    const page = await context.newPage()
    const errors = []
    page.on('pageerror', (error) => errors.push(error.message))
    await page.goto('https://localhost:5443/demo')
    const enter = page.getByRole('button', {
      name: 'Explore Read-Only Demo',
      exact: true,
    })
    await expect(enter).toBeEnabled()
    if (name === 'chromium' && process.argv.includes('--screenshot'))
      await page.screenshot({
        path: fileURLToPath(
          new URL(
            '../../docs/screenshots/portfolio-entry.png',
            import.meta.url,
          ),
        ),
        fullPage: true,
      })
    await enter.click()
    await expect(page.getByLabel('Temporary demo session')).toBeVisible()
    await expect(page.getByText('Connected', { exact: true })).toBeVisible()
    const session = (await context.cookies()).find(
      (cookie) => cookie.name === 'FleetFlow.Session',
    )
    if (!session?.secure || !session.httpOnly || session.sameSite !== 'Strict')
      throw new Error('Production cookie flags failed')
    await page.getByRole('button', { name: 'End demo', exact: true }).click()
    await page
      .getByRole('button', { name: 'End demo and discard edits' })
      .click()
    await expect(page).toHaveURL('https://localhost:5443/demo')
    if (errors.length)
      throw new Error('Production JavaScript error (details withheld)')
    console.log(
      `${name}: local Production SPA/CSP, secure Identity cookie, authenticated SignalR and logout passed`,
    )
    await context.close()
  } finally {
    await browser.close()
  }
}
