import {
  parseTripsJson,
  TripsContractError,
  tripBound,
  tripDate,
  tripGuid,
  tripRecord,
  tripText,
} from './trips.decoder';
import { tripCities, tripInterests, tripPaces } from './trips.types';
import {
  type TripBriefCandidate,
  type TripBriefCapability,
  type TripBriefEvidence,
  type TripBriefField,
  type TripBriefRequest,
  type TripBriefSlot,
  type TripBriefZone,
  tripBriefSlots,
} from './trips-brief.types';

const issueCodes = [
  'partyComposition',
  'adultBounds',
  'adultConflict',
  'destinationChoice',
  'multipleDestinations',
  'cityUnresolved',
  'cityOutsideCatalogue',
  'paceConflict',
  'durationBounds',
  'yearMissing',
  'reversedDates',
  'dateDurationConflict',
  'invalidDate',
  'dateConflict',
  'dateBounds',
  'referenceZoneMissing',
  'dateFormatAmbiguous',
  'weekdayDateConflict',
  'unsupportedDateExpression',
  'unsupportedOrigin',
  'unsupportedMoney',
  'unsupportedChildren',
  'unsupportedAction',
  'unsupportedBooking',
] as const;
const fail = (): never => {
  throw new TripsContractError();
};
const member = <T extends string>(x: unknown, values: readonly T[]): T =>
  typeof x === 'string' && values.includes(x as T) ? (x as T) : fail();
const policy = (x: unknown): string => (typeof x === 'string' && /^[a-f0-9]{64}$/.test(x) ? x : fail());
const publicId = (x: unknown): string => (typeof x === 'string' && /^[\x21-\x7e]{1,128}$/.test(x) ? x : fail());
const zone = (x: unknown): TripBriefZone | null =>
  x === null ? null : member<TripBriefZone>(x, ['Europe/Moscow', 'Europe/Kaliningrad']);
export function decodeTripBriefCapability(value: unknown): TripBriefCapability {
  tripBound(value, 2048);
  const r = tripRecord(value, ['candidateVersion', 'status', 'processorId', 'modelId', 'processingPolicyVersion']);
  if (r['candidateVersion'] !== 1) fail();
  const status = member(r['status'], ['configured', 'unconfigured']);
  if (status === 'unconfigured') {
    if (r['processorId'] !== null || r['modelId'] !== null || r['processingPolicyVersion'] !== null) fail();
    return { candidateVersion: 1, status, processorId: null, modelId: null, processingPolicyVersion: null };
  }
  return {
    candidateVersion: 1,
    status,
    processorId: publicId(r['processorId']),
    modelId: publicId(r['modelId']),
    processingPolicyVersion: policy(r['processingPolicyVersion']),
  };
}
export async function tripBriefPolicyReviewed(c: TripBriefCapability): Promise<boolean> {
  if (c.status !== 'configured' || c.processorId !== 'anthropic' || !c.modelId) return false;
  const bytes = new TextEncoder().encode(JSON.stringify([c.processorId, c.modelId, 'trips-brief-ru-v1']));
  const digest = await globalThis.crypto.subtle.digest('SHA-256', bytes);
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('') === c.processingPolicyVersion;
}
export function tripBriefRequest(value: unknown): TripBriefRequest {
  const r = tripRecord(value, ['requestId', 'text', 'locale', 'referenceZone', 'processingPolicyVersion']);
  const text = tripText(r['text'], 1000, true);
  if (!text.trim() || new TextEncoder().encode(text).length > 4096 || r['locale'] !== 'ru') fail();
  const request: TripBriefRequest = {
    requestId: tripGuid(r['requestId']),
    text,
    locale: 'ru',
    referenceZone: zone(r['referenceZone']),
    processingPolicyVersion: policy(r['processingPolicyVersion']),
  };
  tripBound(request, 8192);
  return request;
}
const instant = (x: unknown): string =>
  typeof x === 'string' &&
  /^\d{4}-\d\d-\d\dT(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(x) &&
  Number.isFinite(Date.parse(x))
    ? (tripDate(x.slice(0, 10)), x)
    : fail();
function list<T>(x: unknown, max: number, decode: (v: unknown) => T): T[] {
  if (!Array.isArray(x) || x.length > max) fail();
  return (x as unknown[]).map(decode);
}
function spans(x: unknown, text: string): TripBriefEvidence[] {
  return list(x, 4, (v) => {
    const r = tripRecord(v, ['start', 'end']);
    const start = r['start'],
      end = r['end'];
    if (
      typeof start !== 'number' ||
      typeof end !== 'number' ||
      !Number.isInteger(start) ||
      !Number.isInteger(end) ||
      start < 0 ||
      end <= start ||
      end > text.length
    )
      fail();
    const split = (n: number) =>
      n > 0 && n < text.length && /[\ud800-\udbff]/.test(text[n - 1]) && /[\udc00-\udfff]/.test(text[n]);
    if (split(start as number) || split(end as number)) fail();
    return { start: start as number, end: end as number };
  });
}
function fieldValue(x: unknown, key: TripBriefSlot): NonNullable<TripBriefField['value']> {
  if (key === 'destinationId') return member(x, tripCities);
  if (key === 'pace') return member(x, tripPaces);
  if (key === 'startDate' || key === 'endDate') return tripDate(x);
  if (key === 'interests') {
    const values = list(x, 5, (v) => member(v, tripInterests));
    if (
      new Set(values).size !== values.length ||
      values.some((v, i) => i > 0 && tripInterests.indexOf(v) <= tripInterests.indexOf(values[i - 1]))
    )
      fail();
    return values;
  }
  return typeof x === 'number' && Number.isInteger(x) && x >= 1 && x <= (key === 'adults' ? 9 : 14) ? x : fail();
}
export function decodeTripBriefCandidate(
  value: unknown,
  request: Pick<TripBriefRequest, 'requestId' | 'text' | 'processingPolicyVersion' | 'referenceZone'>,
): TripBriefCandidate {
  tripBound(value, 16384);
  const r = tripRecord(value, [
    'candidateVersion',
    'requestId',
    'processingPolicyVersion',
    'referenceInstant',
    'referenceZone',
    'referenceLocalDate',
    'expiresAt',
    'fields',
    'issues',
  ]);
  if (
    r['candidateVersion'] !== 1 ||
    tripGuid(r['requestId']) !== request.requestId ||
    policy(r['processingPolicyVersion']) !== request.processingPolicyVersion ||
    zone(r['referenceZone']) !== request.referenceZone
  )
    fail();
  const referenceInstant = instant(r['referenceInstant']),
    expiresAt = instant(r['expiresAt']);
  const ttl = Date.parse(expiresAt) - Date.parse(referenceInstant);
  if (ttl <= 0 || ttl > 600000) fail();
  const referenceZone = zone(r['referenceZone']),
    referenceLocalDate = r['referenceLocalDate'] === null ? null : tripDate(r['referenceLocalDate']);
  if (
    (referenceZone === null && referenceLocalDate !== null) ||
    (referenceZone !== null && referenceLocalDate === null)
  )
    fail();
  if (referenceZone !== null) {
    const local = new Intl.DateTimeFormat('en-CA', {
      timeZone: referenceZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date(referenceInstant));
    if (local !== referenceLocalDate) fail();
  }
  const fs = tripRecord(r['fields'], [...tripBriefSlots]);
  const fields = {} as TripBriefCandidate['fields'];
  for (const key of tripBriefSlots) {
    const f = tripRecord(fs[key], ['state', 'value', 'evidence', 'alternatives']);
    const state = member(f['state'], ['extracted', 'missing', 'ambiguous', 'unsupported', 'invalid']);
    const evidence = spans(f['evidence'], request.text),
      alternatives = list(f['alternatives'], 4, (v) => fieldValue(v, key));
    if (
      (state !== 'extracted' && f['value'] !== null) ||
      (state !== 'missing' && evidence.length === 0) ||
      (state !== 'ambiguous' && alternatives.length > 0) ||
      (state === 'missing' && (evidence.length > 0 || alternatives.length > 0))
    )
      fail();
    fields[key] = { state, value: state === 'extracted' ? fieldValue(f['value'], key) : null, evidence, alternatives };
  }
  const issues = list(r['issues'], 16, (v) => {
    const issue = tripRecord(v, ['code', 'slots', 'evidence']);
    const code = issue['code'];
    member(code, issueCodes);
    const slots = list(issue['slots'], 7, (v) => member(v, tripBriefSlots));
    if (new Set(slots).size !== slots.length) fail();
    const evidence = spans(issue['evidence'], request.text);
    if (evidence.length === 0) fail();
    return { code: code as string, slots, evidence };
  });
  return {
    candidateVersion: 1,
    requestId: request.requestId,
    processingPolicyVersion: request.processingPolicyVersion,
    referenceInstant,
    referenceZone,
    referenceLocalDate,
    expiresAt,
    fields,
    issues,
  };
}
/** Reject duplicate JSON members before the ordinary strict shape decoder. No private parse details escape. */
export function parseTripBriefJson(raw: string, max: number): unknown {
  const value = parseTripsJson(raw, max);
  let i = 0;
  const ws = () => {
    while (/\s/.test(raw[i] ?? '') && i < raw.length) i++;
  };
  const string = (): string => {
    const start = i++;
    while (i < raw.length) {
      if (raw[i] === '\\') {
        i += 2;
        continue;
      }
      if (raw[i++] === '"') return JSON.parse(raw.slice(start, i));
    }
    return fail();
  };
  const scan = (): void => {
    ws();
    if (raw[i] === '{') {
      i++;
      ws();
      const keys = new Set<string>();
      if (raw[i] === '}') {
        i++;
        return;
      }
      for (;;) {
        ws();
        const key = string();
        if (keys.has(key)) fail();
        keys.add(key);
        ws();
        i++;
        scan();
        ws();
        if (raw[i++] === '}') return;
      }
    } else if (raw[i] === '[') {
      i++;
      ws();
      if (raw[i] === ']') {
        i++;
        return;
      }
      for (;;) {
        scan();
        ws();
        if (raw[i++] === ']') return;
      }
    } else if (raw[i] === '"') string();
    else while (i < raw.length && !/[\s,}\]]/.test(raw[i])) i++;
  };
  scan();
  return value;
}
