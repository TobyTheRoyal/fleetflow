import { act, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { InsightsPage } from './InsightsPage'
import { renderPage } from '@/test/render'
import { json, mockApi } from '@/test/http'
import { page } from '@/test/fixtures'
import { notificationHandler } from '@/features/realtime/notifications'
const id = '11111111-1111-4111-8111-111111111111'
const timestamp = '2026-11-08T12:00:00Z'
const finding = {
  findingId: id + ':InTransitOverdue',
  shipmentId: id,
  shipmentNumber: 'FF-2026-000001',
  type: 'InTransitOverdue',
  severity: 'Critical',
  explanation: 'Recorded in transit past its deadline.',
  evidence: {
    status: 'InTransit',
    version: 3,
    pickupUtc: '2026-11-06T08:00:00Z',
    deadlineUtc: '2026-11-07T08:00:00Z',
    driverAssigned: true,
    vehicleAssigned: true,
    driverEligible: true,
    vehicleEligible: true,
  },
  recommendedAction: 'Verify actual progress with the dispatcher.',
  timestampUtc: timestamp,
}
const summary = {
  asOfUtc: timestamp,
  totalFindings: 1,
  affectedShipments: 1,
  overdueShipments: 1,
  dueSoonShipments: 0,
  criticalFindings: 1,
  highFindings: 0,
  findingsByType: { InTransitOverdue: 1 },
  snapshotFingerprint: 'a'.repeat(64),
  aiSampleSize: 1,
}
const budget = {
  userLimit: 3,
  userRemaining: 3,
  globalLimit: 20,
  globalRemaining: 20,
  resetsAtUtc: '2026-11-09T00:00:00Z',
  inProgress: false,
  retryAfterUtc: null,
}
const available = {
  canGenerate: true,
  code: 'ai_available',
  explanation: 'Available for on-demand use.',
  model: '@cf/zai-org/glm-4.7-flash',
  verificationExpiresAtUtc: '2026-11-09T00:00:00Z',
  budget,
}
const analysis = {
  generatedAtUtc: timestamp,
  snapshotFingerprint: summary.snapshotFingerprint,
  model: available.model,
  cached: false,
  label: 'AI-assisted prioritization',
  summary: 'Prioritize verified overdue transport findings.',
  sampleSize: 1,
  totalFindings: 1,
  recommendations: [
    {
      findingId: finding.findingId,
      shipmentId: id,
      shipmentNumber: finding.shipmentNumber,
      severity: 'Critical',
      verifiedExplanation: finding.explanation,
      suggestedAction: finding.recommendedAction,
    },
  ],
  budget: { ...budget, userRemaining: 2, globalRemaining: 19 },
}
afterEach(() => vi.unstubAllGlobals())
function handler(path: string) {
  return json(
    path.endsWith('/summary')
      ? summary
      : path.endsWith('/ai-availability')
        ? available
        : page([finding]),
  )
}
it('renders evidence and shipment links without automatically generating AI', async () => {
  const calls = mockApi(handler)
  renderPage(<InsightsPage />, '/insights')
  expect(
    await screen.findByRole('heading', { name: 'In transit past deadline' }),
  ).toBeVisible()
  expect(screen.getByRole('link', { name: /FF-2026-000001/ })).toHaveAttribute(
    'href',
    `/shipments/${id}`,
  )
  await userEvent.click(screen.getByText('Supporting evidence'))
  expect(screen.getByText('In transit · Version 3')).toBeVisible()
  expect(calls.mock.calls.some(([url]) => url.endsWith('/ai-analysis'))).toBe(
    false,
  )
})
it('generates only on click, sends the current fingerprint and updates quota', async () => {
  let generated = false
  const calls = mockApi((path) => {
    if (path.endsWith('/ai-analysis')) {
      generated = true
      return json(analysis)
    }
    if (path.endsWith('/ai-availability'))
      return json({
        ...available,
        budget: generated ? analysis.budget : budget,
      })
    return handler(path)
  })
  renderPage(<InsightsPage />, '/insights')
  const button = screen.getByRole('button', { name: 'Generate AI Analysis' })
  await waitFor(() => expect(button).toBeEnabled())
  await userEvent.click(button)
  expect(await screen.findByText(analysis.summary)).toBeVisible()
  expect(screen.getByText('AI-selected suggestion:')).toBeVisible()
  await waitFor(() =>
    expect(screen.getByLabelText('Daily AI allowance')).toHaveTextContent(
      '2 of 3',
    ),
  )
  const commands = calls.mock.calls.filter(([url]) =>
    url.endsWith('/ai-analysis'),
  )
  expect(commands).toHaveLength(1)
  expect(JSON.parse(String(commands[0]?.[1].body))).toEqual({
    snapshotFingerprint: summary.snapshotFingerprint,
  })
})
it.each([
  ['ai_disabled', 'External AI is disabled.'],
  ['ai_not_authorized', 'This account is not authorized.'],
  ['ai_user_budget_exhausted', 'Your daily allowance is exhausted.'],
  ['ai_provider_budget_exhausted', 'The provider allowance is exhausted.'],
  ['ai_credentials_missing', 'Provider credentials are not configured.'],
])('keeps deterministic insights usable when %s', async (code, explanation) => {
  mockApi((path) =>
    path.endsWith('/ai-availability')
      ? json({ ...available, canGenerate: false, code, explanation })
      : handler(path),
  )
  renderPage(<InsightsPage />, '/insights')
  expect(
    await screen.findByRole('heading', { name: 'In transit past deadline' }),
  ).toBeVisible()
  expect(await screen.findByText(new RegExp(explanation))).toBeVisible()
  expect(
    screen.getByRole('button', { name: 'Generate AI Analysis' }),
  ).toBeDisabled()
})
it('reports HTTP 429 without retrying generation', async () => {
  const calls = mockApi((path) =>
    path.endsWith('/ai-analysis')
      ? json(
          {
            code: 'ai_user_budget_exhausted',
            detail: 'Your UTC daily AI allowance is exhausted.',
          },
          429,
        )
      : handler(path),
  )
  renderPage(<InsightsPage />, '/insights')
  const button = screen.getByRole('button', { name: 'Generate AI Analysis' })
  await waitFor(() => expect(button).toBeEnabled())
  await userEvent.click(button)
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Your UTC daily AI allowance is exhausted.',
  )
  expect(
    calls.mock.calls.filter(([url]) => url.endsWith('/ai-analysis')),
  ).toHaveLength(1)
})
it('prevents duplicate submissions while generation is pending', async () => {
  const calls = mockApi((path) =>
    path.endsWith('/ai-analysis') ? new Promise(() => {}) : handler(path),
  )
  renderPage(<InsightsPage />, '/insights')
  const button = screen.getByRole('button', { name: 'Generate AI Analysis' })
  await waitFor(() => expect(button).toBeEnabled())
  await userEvent.dblClick(button)
  expect(
    screen.getByRole('button', { name: 'Generating analysis…' }),
  ).toBeDisabled()
  expect(
    calls.mock.calls.filter(([url]) => url.endsWith('/ai-analysis')),
  ).toHaveLength(1)
})
it('refreshes deterministic data on SignalR notifications without inference', async () => {
  const calls = mockApi(handler)
  const { client } = renderPage(<InsightsPage />, '/insights')
  await screen.findByRole('heading', { name: 'In transit past deadline' })
  const before = calls.mock.calls.filter(([url]) =>
    url.endsWith('/summary'),
  ).length
  await act(async () => {
    notificationHandler(client)({
      eventId: '22222222-2222-4222-8222-222222222222',
      entityType: 'shipment',
      entityId: id,
      eventType: 'ShipmentStatusChanged',
      version: 4,
      timestampUtc: timestamp,
    })
  })
  await waitFor(() =>
    expect(
      calls.mock.calls.filter(([url]) => url.endsWith('/summary')).length,
    ).toBeGreaterThan(before),
  )
  expect(calls.mock.calls.some(([url]) => url.endsWith('/ai-analysis'))).toBe(
    false,
  )
})
it('labels an existing analysis stale after the operational snapshot changes', async () => {
  let updated = false
  mockApi((path) =>
    path.endsWith('/ai-analysis')
      ? json(analysis)
      : path.endsWith('/summary')
        ? json({
            ...summary,
            snapshotFingerprint: updated
              ? 'b'.repeat(64)
              : summary.snapshotFingerprint,
          })
        : handler(path),
  )
  const { client } = renderPage(<InsightsPage />, '/insights')
  const button = screen.getByRole('button', { name: 'Generate AI Analysis' })
  await waitFor(() => expect(button).toBeEnabled())
  await userEvent.click(button)
  await screen.findByText(analysis.summary)
  updated = true
  await act(async () => {
    await client.invalidateQueries({ queryKey: ['insights'] })
  })
  expect(
    await screen.findByText(/analysis describes an earlier snapshot/),
  ).toBeVisible()
})
it('keeps a manual recovery path for failed deterministic reads', async () => {
  mockApi((path) =>
    path.endsWith('/summary')
      ? json(summary)
      : path.endsWith('/ai-availability')
        ? json(available)
        : json({ detail: 'Database unavailable.' }, 503),
  )
  renderPage(<InsightsPage />, '/insights')
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Database unavailable.',
  )
  expect(screen.getByRole('button', { name: 'Refresh findings' })).toBeVisible()
})
it('shows an honest empty state without fake AI output', async () => {
  mockApi((path) =>
    path.endsWith('/summary')
      ? json({ ...summary, totalFindings: 0, aiSampleSize: 0 })
      : path.endsWith('/ai-availability')
        ? json(available)
        : json(page([])),
  )
  renderPage(<InsightsPage />, '/insights')
  expect(
    await screen.findByRole('heading', {
      name: 'No current operational alerts',
    }),
  ).toBeVisible()
  expect(
    screen.getByRole('button', { name: 'Generate AI Analysis' }),
  ).toBeDisabled()
})
it('shows loading skeletons while API requests are pending', () => {
  mockApi(() => new Promise(() => {}))
  renderPage(<InsightsPage />, '/insights')
  expect(screen.getAllByLabelText('Loading data').length).toBeGreaterThan(0)
})
