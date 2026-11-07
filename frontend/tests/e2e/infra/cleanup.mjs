import { execFileSync } from 'node:child_process'
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
if (process.env.CI !== 'true' && !process.argv.includes('--confirm'))
  throw new Error(
    'This removes only disposable fleetflow-e2e data. Pass --confirm for local cleanup.',
  )
const dir = mkdtempSync(join(tmpdir(), 'fleetflow-e2e-cleanup-'))
const file = join(dir, '.env')
try {
  writeFileSync(file, '', { mode: 0o600 })
  execFileSync(
    'docker',
    [
      'compose',
      '-p',
      'fleetflow-e2e',
      '--env-file',
      file,
      '-f',
      resolve('../compose.yaml'),
      'down',
      '--volumes',
      '--remove-orphans',
    ],
    {
      stdio: 'inherit',
      env: {
        ...process.env,
        POSTGRES_PASSWORD: 'unused-cleanup-placeholder',
        POSTGRES_DB: 'fleetflow_e2e',
        POSTGRES_USER: 'fleetflow',
        POSTGRES_PORT: '5434',
        API_PORT: '5180',
      },
    },
  )
} finally {
  rmSync(dir, { recursive: true, force: true })
}
