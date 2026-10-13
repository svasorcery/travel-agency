import { SupportContractError, supportGuid, supportRevision } from './support.decoder';
import { supportCategories } from './support.types';
import {
  type SupportClassificationSuggestion,
  type SupportSuggestedCategory,
  type SupportUrgencySuggestion,
  supportClassificationReasons,
} from './support-classification.types';

function object(value: unknown, keys: string[]): Record<string, unknown> {
  if (
    !value ||
    typeof value !== 'object' ||
    Array.isArray(value) ||
    Object.keys(value).some((key) => !keys.includes(key)) ||
    keys.some((key) => !Object.hasOwn(value, key))
  )
    throw new SupportContractError();
  return value as Record<string, unknown>;
}
function member<T extends string>(value: unknown, choices: readonly T[]): T {
  if (typeof value !== 'string' || !choices.includes(value as T)) throw new SupportContractError();
  return value as T;
}
function date(value: unknown): string {
  if (
    typeof value !== 'string' ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value) ||
    !Number.isFinite(Date.parse(value)) ||
    new Date(Date.parse(value)).toISOString().slice(0, 19) !== value.slice(0, 19)
  )
    throw new SupportContractError();
  return value;
}
export function supportClassificationRequestId(value: unknown): string {
  const id = supportGuid(value);
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(id))
    throw new SupportContractError();
  return id;
}
function category(value: unknown): SupportSuggestedCategory | null {
  if (value === null) return null;
  const decoded = member(value, supportCategories);
  if (decoded === 'Unclassified') throw new SupportContractError();
  return decoded;
}
function streamVersion(value: unknown): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0) throw new SupportContractError();
  return value;
}
function urgency(value: unknown, generated: number): SupportUrgencySuggestion {
  const r = object(value, ['kind', 'nextScheduledDepartureAt', 'observedAt', 'streamVersion', 'ruleVersion']);
  const result: SupportUrgencySuggestion = {
    kind: member(r['kind'], ['Unknown', 'KeepCurrent', 'High']),
    nextScheduledDepartureAt: r['nextScheduledDepartureAt'] === null ? null : date(r['nextScheduledDepartureAt']),
    observedAt: r['observedAt'] === null ? null : date(r['observedAt']),
    streamVersion: r['streamVersion'] === null ? null : streamVersion(r['streamVersion']),
    ruleVersion: member(r['ruleVersion'], ['support-urgency-v1']),
  };
  if (result.kind === 'Unknown') {
    if (result.nextScheduledDepartureAt !== null || result.observedAt !== null || result.streamVersion !== null)
      throw new SupportContractError();
  } else {
    if (
      result.observedAt === null ||
      result.streamVersion === null ||
      Date.parse(result.observedAt) > generated ||
      generated - Date.parse(result.observedAt) > 30000
    )
      throw new SupportContractError();
    if (result.nextScheduledDepartureAt !== null && Date.parse(result.nextScheduledDepartureAt) <= generated)
      throw new SupportContractError();
    if (
      result.kind === 'High' &&
      (result.nextScheduledDepartureAt === null || Date.parse(result.nextScheduledDepartureAt) - generated > 86400000)
    )
      throw new SupportContractError();
  }
  return result;
}
export function decodeSupportClassificationSuggestion(
  value: unknown,
  expectedCaseId: string,
  expectedRequestId: string,
  expectedRevision: number,
  now = Date.now(),
): SupportClassificationSuggestion {
  const r = object(value, [
    'caseId',
    'requestId',
    'capturedRevision',
    'generatedAt',
    'expiresAt',
    'status',
    'reason',
    'category',
    'queue',
    'servingProfileId',
    'questionVersion',
    'urgency',
  ]);
  const generatedAt = date(r['generatedAt']),
    expiresAt = date(r['expiresAt']);
  const generated = Date.parse(generatedAt),
    expires = Date.parse(expiresAt);
  if (
    !Number.isFinite(now) ||
    generated > now ||
    expires <= now ||
    expires <= generated ||
    expires - generated > 120000
  )
    throw new SupportContractError();
  const profile = r['servingProfileId'];
  if (profile !== null && (typeof profile !== 'string' || !/^[0-9a-f]{64}$/.test(profile)))
    throw new SupportContractError();
  const result: SupportClassificationSuggestion = {
    caseId: supportGuid(r['caseId']),
    requestId: supportClassificationRequestId(r['requestId']),
    capturedRevision: supportRevision(r['capturedRevision']),
    generatedAt,
    expiresAt,
    status: member(r['status'], ['Disabled', 'ManualRequired', 'Candidate']),
    reason: member(r['reason'], supportClassificationReasons),
    category: category(r['category']),
    queue: category(r['queue']),
    servingProfileId: profile as string | null,
    questionVersion:
      r['questionVersion'] === null ? null : member(r['questionVersion'], ['support-category-v1'] as const),
    urgency: urgency(r['urgency'], generated),
  };
  if (
    result.caseId !== supportGuid(expectedCaseId) ||
    result.requestId !== supportClassificationRequestId(expectedRequestId) ||
    result.capturedRevision !== supportRevision(expectedRevision) ||
    (result.servingProfileId === null) !== (result.questionVersion === null)
  )
    throw new SupportContractError();
  if (result.status === 'Candidate') {
    if (
      result.reason !== 'Ready' ||
      result.category === null ||
      result.queue !== result.category ||
      result.servingProfileId === null ||
      result.questionVersion === null
    )
      throw new SupportContractError();
  } else {
    if (result.category !== null || result.queue !== null) throw new SupportContractError();
    if (
      result.status === 'Disabled'
        ? result.reason !== 'Disabled' ||
          result.servingProfileId !== null ||
          result.questionVersion !== null ||
          result.urgency.kind !== 'Unknown'
        : result.reason === 'Ready' || result.reason === 'Disabled'
    )
      throw new SupportContractError();
  }
  return result;
}
