import { execFileSync } from 'node:child_process'
import { mkdtempSync, writeFileSync, rmSync, mkdirSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { randomBytes } from 'node:crypto'
export function compose(...args: string[]) {
  const file = process.env.FLEETFLOW_E2E_ENV
  if (!file) throw new Error('Isolated E2E stack was not initialized.')
  return execFileSync(
    'docker',
    [
      'compose',
      '-p',
      'fleetflow-e2e',
      '--env-file',
      file,
      '-f',
      resolve('../compose.yaml'),
      ...args,
    ],
    { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] },
  )
}
export default async function setup() {
  const dir = mkdtempSync(join(tmpdir(), 'fleetflow-e2e-'))
  process.env.FLEETFLOW_E2E_ENV = join(dir, '.env')
  const password = randomBytes(24).toString('hex')
  writeFileSync(
    process.env.FLEETFLOW_E2E_ENV,
    `POSTGRES_DB=fleetflow_e2e
POSTGRES_USER=fleetflow
POSTGRES_PASSWORD=${password}
POSTGRES_PORT=5434
API_PORT=5180
`,
    { mode: 0o600 },
  )
  let ownsStack = false
  try {
    if (compose('ps', '-aq').trim())
      throw new Error('An E2E stack is already running.')
    ownsStack = true
    console.log('Starting isolated PostgreSQL/API E2E stack (5180 / 5434)…')
    compose('up', '--build', '-d', '--wait', '--wait-timeout', '120')
    compose('run', '--rm', '--no-deps', 'api', '--migrate', '--seed')
    const deadline = Date.now() + 60000
    while (true) {
      try {
        if (
          (
            await fetch('http://127.0.0.1:5180/health/ready', {
              signal: AbortSignal.timeout(2000),
            })
          ).ok
        )
          break
      } catch {
        /* startup */
      }
      if (Date.now() > deadline)
        throw new Error('E2E API did not become ready.')
      await new Promise((resolve) => setTimeout(resolve, 500))
    }
    if (process.env.CHECK_API_CONTRACT === '1')
      execFileSync('npm', ['run', 'check:api'], {
        stdio: 'inherit',
        env: {
          ...process.env,
          OPENAPI_URL: 'http://127.0.0.1:5180/openapi/v1.json',
        },
      })
    const diagnostics = () => {
      if (!process.env.CI) return
      try {
        mkdirSync('test-results', { recursive: true })
        const logs = compose('logs', '--no-color', '--tail', '100', 'api')
          .replaceAll(password, '[REDACTED]')
          .replace(/Password=[^;\s]+/gi, 'Password=[REDACTED]')
        writeFileSync('test-results/api.log', logs)
      } catch {
        /* Diagnostics must not prevent cleanup. */
      }
    }
    return async () => {
      diagnostics()
      try {
        compose('down', '--volumes', '--remove-orphans')
      } finally {
        rmSync(dir, { recursive: true, force: true })
      }
    }
  } catch {
    try {
      if (ownsStack) compose('down', '--volumes', '--remove-orphans')
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
    // Do not attach raw Docker output: it can contain generated connection credentials.
    throw new Error(
      'E2E stack setup failed. Check Docker, an existing fleetflow-e2e project and ports 5180/5434.',
    )
  }
}
