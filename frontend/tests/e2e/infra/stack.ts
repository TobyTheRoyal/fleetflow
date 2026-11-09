import { request } from '@playwright/test'
import { execFileSync } from 'node:child_process'
import {
  mkdtempSync,
  writeFileSync,
  readFileSync,
  rmSync,
  mkdirSync,
} from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { randomBytes } from 'node:crypto'
import { parseEnv } from 'node:util'
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
      '-f',
      resolve('../compose.e2e.yaml'),
      ...args,
    ],
    {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
      timeout: 300000,
      // Shell exports otherwise override --env-file, potentially targeting dev ports.
      env: { ...process.env, ...parseEnv(readFileSync(file, 'utf8')) },
    },
  )
}
export default async function setup() {
  const dir = mkdtempSync(join(tmpdir(), 'fleetflow-e2e-'))
  process.env.FLEETFLOW_E2E_ENV = join(dir, '.env')
  process.env.FLEETFLOW_E2E_AUTH_DIR = dir
  const demoPasswords = Object.fromEntries(
    ['Admin', 'Dispatcher', 'Viewer'].map((role) => [
      role,
      'Ff!7' + randomBytes(24).toString('hex'),
    ]),
  )
  const password = randomBytes(24).toString('hex')
  writeFileSync(
    process.env.FLEETFLOW_E2E_ENV,
    `POSTGRES_DB=fleetflow_e2e
POSTGRES_USER=fleetflow
POSTGRES_PASSWORD=${password}
POSTGRES_PORT=5434
API_PORT=5180
AI_ENABLED=false
CLOUDFLARE_API_TOKEN=
DEMO_ADMIN_PASSWORD=${demoPasswords.Admin}
DEMO_DISPATCHER_PASSWORD=${demoPasswords.Dispatcher}
DEMO_VIEWER_PASSWORD=${demoPasswords.Viewer}
`,
    { mode: 0o600 },
  )
  writeFileSync(join(dir, 'credentials.json'), JSON.stringify(demoPasswords), {
    mode: 0o600,
  })
  let ownsStack = false
  let stage = 'checking for an existing test project'
  const diagnostics = () => {
    if (!process.env.CI || !ownsStack) return
    try {
      mkdirSync('test-results', { recursive: true })
      const logs = [
        `E2E stage: ${stage}`,
        compose('ps', '--all'),
        compose(
          'logs',
          '--no-color',
          '--tail',
          '100',
          'api',
          'demo',
          'postgres',
        ),
      ]
        .join('\n')
        .replaceAll(password, '[REDACTED]')
        .replace(/Ff!7[a-f0-9]{48}/g, '[REDACTED]')
        .replace(/Password=[^;\s]+/gi, 'Password=[REDACTED]')
      writeFileSync('test-results/stack.log', logs, { mode: 0o600 })
    } catch {
      /* Diagnostics must not prevent cleanup. */
    }
  }
  try {
    if (compose('ps', '-aq').trim())
      throw new Error('An E2E stack is already running.')
    ownsStack = true
    stage = 'building and starting PostgreSQL/API'
    console.log('Starting isolated PostgreSQL/API E2E stack (5180 / 5434)…')
    compose('up', '--build', '-d', '--wait', '--wait-timeout', '120')
    stage = 'applying migration and development seed'
    compose(
      'run',
      '--rm',
      '--no-deps',
      '--entrypoint',
      'dotnet',
      'api',
      'FleetFlow.Api.dll',
      '--migrate',
      '--seed',
      '--seed-users',
    )
    stage = 'waiting for database-backed API readiness'
    const deadline = Date.now() + 60000
    while (true) {
      try {
        const readiness = await Promise.all(
          [5180, 5181].map((port) =>
            fetch(`http://127.0.0.1:${port}/health/ready`, {
              signal: AbortSignal.timeout(2000),
            }),
          ),
        )
        if (readiness.every((response) => response.ok)) break
      } catch {
        /* startup */
      }
      if (Date.now() > deadline)
        throw new Error('E2E API did not become ready.')
      await new Promise((resolve) => setTimeout(resolve, 500))
    }
    if (
      process.env.CHECK_API_CONTRACT === '1' ||
      process.env.UPDATE_API_CONTRACT === '1'
    ) {
      stage = 'checking generated API contract'
      execFileSync(
        'npm',
        [
          'run',
          process.env.UPDATE_API_CONTRACT === '1'
            ? 'generate:api'
            : 'check:api',
        ],
        {
          stdio: 'inherit',
          env: {
            ...process.env,
            OPENAPI_URL: 'http://127.0.0.1:5180/openapi/v1.json',
          },
        },
      )
    }
    stage = 'provisioning authenticated test contexts'
    for (const role of ['Admin', 'Dispatcher', 'Viewer']) {
      const client = await request.newContext({
        baseURL: 'http://127.0.0.1:5180',
      })
      try {
        const csrf = (await (await client.get('/api/v1/auth/csrf')).json()) as {
          token: string
        }
        const login = await client.post('/api/v1/auth/login', {
          data: {
            email: `${role.toLowerCase()}@fleetflow.example`,
            password: demoPasswords[role],
          },
          headers: { 'X-CSRF-TOKEN': csrf.token },
        })
        if (!login.ok()) throw new Error('Test login failed.')
        await client.storageState({ path: join(dir, role + '.json') })
      } finally {
        await client.dispose()
      }
    }
    stage = 'running browser tests'
    return async () => {
      diagnostics()
      try {
        compose('down', '--volumes', '--remove-orphans')
      } finally {
        rmSync(dir, { recursive: true, force: true })
      }
    }
  } catch {
    diagnostics()
    try {
      if (ownsStack) compose('down', '--volumes', '--remove-orphans')
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
    // Do not attach raw Docker output: it can contain generated connection credentials.
    throw new Error(
      `E2E stack setup failed while ${stage}. Check Docker, an existing fleetflow-e2e project and ports 5180/5434. CI diagnostics: test-results/stack.log.`,
    )
  }
}
