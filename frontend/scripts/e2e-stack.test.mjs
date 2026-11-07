import { test } from 'node:test'
import assert from 'node:assert/strict'
import {
  mkdtempSync,
  mkdirSync,
  writeFileSync,
  readFileSync,
  existsSync,
  rmSync,
} from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import setup from '../tests/e2e/infra/stack.ts'

async function withFakeDocker(scenario, action) {
  const directory = mkdtempSync(join(tmpdir(), 'fleetflow-stack-test-'))
  const previousDirectory = process.cwd()
  const previousEnvironment = { ...process.env }
  const calls = join(directory, 'calls.jsonl')
  try {
    mkdirSync(join(directory, 'frontend'))
    // No real Docker access: exercise failure diagnostics and cleanup targeting.
    writeFileSync(
      join(directory, 'docker'),
      `#!/usr/bin/env node
      const fs = require('node:fs');
      const args = process.argv.slice(2);
      fs.appendFileSync(process.env.FAKE_CALLS, JSON.stringify({args,
        port: process.env.API_PORT, pgPort: process.env.POSTGRES_PORT,
        database: process.env.POSTGRES_DB}) + '\\n');
      if (args.includes('ps') && args.includes('-aq') && process.env.FAKE_SCENARIO === 'existing')
        process.stdout.write('another-test-container');
      if (args.includes('up')) process.exit(17);
      if (args.includes('logs')) process.stdout.write('Password=' + process.env.POSTGRES_PASSWORD);
    `,
      { mode: 0o700 },
    )
    Object.assign(process.env, {
      PATH: directory + ':' + process.env.PATH,
      CI: 'true',
      API_PORT: '5080',
      POSTGRES_PORT: '5433',
      POSTGRES_DB: 'must_not_be_used',
      FAKE_CALLS: calls,
      FAKE_SCENARIO: scenario,
    })
    process.chdir(join(directory, 'frontend'))
    await action(() =>
      readFileSync(calls, 'utf8').trim().split('\n').map(JSON.parse),
    )
  } finally {
    process.chdir(previousDirectory)
    for (const key of Object.keys(process.env)) {
      if (!(key in previousEnvironment)) delete process.env[key]
    }
    Object.assign(process.env, previousEnvironment)
    rmSync(directory, { recursive: true, force: true })
  }
}

test('failed startup keeps isolated configuration, redacts logs and cleans only its project', async () => {
  await withFakeDocker('failure', async (calls) => {
    await assert.rejects(setup(), /while building and starting PostgreSQL\/API/)
    const recorded = calls()
    assert.ok(
      recorded.every(
        (call) =>
          call.port === '5180' &&
          call.pgPort === '5434' &&
          call.database === 'fleetflow_e2e',
      ),
    )
    assert.ok(
      recorded.every(
        (call) => call.args[1] === '-p' && call.args[2] === 'fleetflow-e2e',
      ),
    )
    assert.ok(
      recorded.some(
        (call) => call.args.includes('down') && call.args.includes('--volumes'),
      ),
    )
    const log = readFileSync('test-results/stack.log', 'utf8')
    assert.match(log, /Password=\[REDACTED\]/)
    assert.doesNotMatch(log, /[a-f0-9]{48}/)
    assert.equal(existsSync(process.env.FLEETFLOW_E2E_ENV), false)
  })
})

test('an existing test project is neither inspected for logs nor removed', async () => {
  await withFakeDocker('existing', async (calls) => {
    await assert.rejects(setup(), /checking for an existing test project/)
    assert.equal(calls().length, 1)
    assert.ok(calls()[0].args.includes('ps'))
    assert.equal(existsSync('test-results/stack.log'), false)
    assert.equal(existsSync(process.env.FLEETFLOW_E2E_ENV), false)
  })
})
