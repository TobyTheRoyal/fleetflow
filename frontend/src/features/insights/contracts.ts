import { z } from 'zod'
import type { components } from '@/types/api.generated'
import { statuses } from '@/types/api'
const timestamp = z.iso.datetime({ offset: true })
export const severitySchema = z.enum(['Low', 'Medium', 'High', 'Critical'])
export const findingSchema = z.object({
  findingId: z.string(),
  shipmentId: z.uuid(),
  shipmentNumber: z.string(),
  type: z.enum([
    'Overdue',
    'InTransitOverdue',
    'DueSoon',
    'PickupWindowOpen',
    'UnassignedDraft',
    'AssignmentRequiresReview',
  ]),
  severity: severitySchema,
  explanation: z.string(),
  evidence: z.object({
    status: z.enum(statuses),
    version: z.number().int(),
    pickupUtc: timestamp,
    deadlineUtc: timestamp,
    driverAssigned: z.boolean(),
    vehicleAssigned: z.boolean(),
    driverEligible: z.boolean(),
    vehicleEligible: z.boolean(),
  }),
  recommendedAction: z.string(),
  timestampUtc: timestamp,
}) satisfies z.ZodType<components['schemas']['OperationalFinding']>
export const summarySchema = z.object({
  asOfUtc: timestamp,
  totalFindings: z.number().int(),
  affectedShipments: z.number().int(),
  overdueShipments: z.number().int(),
  dueSoonShipments: z.number().int(),
  criticalFindings: z.number().int(),
  highFindings: z.number().int(),
  findingsByType: z.record(z.string(), z.number().int()),
  snapshotFingerprint: z.string(),
  aiSampleSize: z.number().int(),
}) satisfies z.ZodType<components['schemas']['InsightSummary']>
export const budgetSchema = z.object({
  userLimit: z.number().int(),
  userRemaining: z.number().int(),
  globalLimit: z.number().int(),
  globalRemaining: z.number().int(),
  resetsAtUtc: timestamp,
  inProgress: z.boolean(),
  retryAfterUtc: timestamp.nullable(),
  providerBlockCode: z.string().nullable().optional(),
}) satisfies z.ZodType<components['schemas']['AiBudget']>
export const availabilitySchema = z.object({
  canGenerate: z.boolean(),
  code: z.string(),
  explanation: z.string(),
  model: z.string(),
  verificationExpiresAtUtc: timestamp.nullable(),
  budget: budgetSchema,
}) satisfies z.ZodType<components['schemas']['AiAvailability']>
export const analysisSchema = z.object({
  generatedAtUtc: timestamp,
  snapshotFingerprint: z.string(),
  model: z.string(),
  cached: z.boolean(),
  label: z.string(),
  summary: z.string(),
  sampleSize: z.number().int(),
  totalFindings: z.number().int(),
  recommendations: z.array(
    z.object({
      findingId: z.string(),
      shipmentId: z.uuid(),
      shipmentNumber: z.string(),
      severity: severitySchema,
      verifiedExplanation: z.string(),
      suggestedAction: z.string(),
    }),
  ),
  budget: budgetSchema,
}) satisfies z.ZodType<components['schemas']['AiAnalysis']>
export type Finding = z.infer<typeof findingSchema>
