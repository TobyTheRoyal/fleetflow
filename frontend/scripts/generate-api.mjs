import { execFileSync } from 'node:child_process'
// Run the pinned generator in its own npm environment (it currently requires TS 5).
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
    'src/types/api.generated.ts',
  ],
  { stdio: 'inherit' },
)
