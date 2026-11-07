import { execFileSync } from 'node:child_process'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
const check = process.argv.includes('--check')
const temporary = check
  ? mkdtempSync(join(tmpdir(), 'fleetflow-contract-'))
  : null
const output = temporary
  ? join(temporary, 'api.ts')
  : 'src/types/api.generated.ts'
try {
  // The pinned generator runs in its own npm environment: its CLI still uses TS 5.
  execFileSync(
    'npm',
    [
      'exec',
      '--yes',
      '--package=openapi-typescript@7.13.0',
      '--',
      'openapi-typescript',
      process.env.OPENAPI_URL ?? 'http://127.0.0.1:5080/openapi/v1.json',
      '-o',
      output,
    ],
    { stdio: 'inherit' },
  )
  if (
    check &&
    readFileSync(output, 'utf8') !==
      readFileSync('src/types/api.generated.ts', 'utf8')
  )
    throw new Error(
      'OpenAPI types differ. Run npm run generate:api and review the contract change.',
    )
} finally {
  if (temporary) rmSync(temporary, { recursive: true, force: true })
}
