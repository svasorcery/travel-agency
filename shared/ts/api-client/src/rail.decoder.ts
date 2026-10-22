import type {
  RailAttribution,
  RailJourney,
  RailKind,
  RailMode,
  RailPrice,
  RailProfile,
  RailRef,
  RailRequest,
  RailSearchResult,
  RailStation,
  RailSuggestions,
  RailTickets,
} from './rail.types';
export class RailContractError extends Error {
  constructor() {
    super('Invalid Rail response.');
  }
}
export class RailProfileNoticeError extends RailContractError {
  readonly code = 'Rail.Profile.Changed';
}
// Rail-only companion decoder shares the existing strict primitives, not public provider DTOs.
export const railDecoding = {
  object,
  hasControl,
  text,
  member,
  array,
  integer,
  reasons,
  guid,
  time,
  ref,
  sameRef,
  attribution,
  request,
  sameRequest,
  fail,
};
export interface RailExpectedAuthority {
  profile: RailProfile;
  profileEpoch: string;
  expiresAt: string | null;
}
function fail(): never {
  throw new RailContractError();
}
function object(value: unknown, keys: string[]): Record<string, unknown> {
  if (
    !value ||
    typeof value !== 'object' ||
    Array.isArray(value) ||
    Object.keys(value).some((k) => !keys.includes(k)) ||
    keys.some((k) => !Object.hasOwn(value, k))
  )
    fail();
  return value as Record<string, unknown>;
}
function text(value: unknown, bytes = 1024): string {
  if (typeof value !== 'string' || new TextEncoder().encode(value).length > bytes || hasControl(value, true)) fail();
  return value;
}
function hasControl(value: string, allowLayout = false): boolean {
  return Array.from(value).some((character) => {
    const code = character.charCodeAt(0);
    return (code < 32 && !(allowLayout && [9, 10, 13].includes(code))) || (code >= 127 && code <= 159);
  });
}
function nullableText(value: unknown): string | null {
  return value === null ? null : text(value);
}
function member<const T extends string>(value: unknown, choices: readonly T[]): T {
  if (typeof value !== 'string' || !choices.includes(value as T)) fail();
  return value as T;
}
function array(value: unknown, max: number): unknown[] {
  if (!Array.isArray(value) || value.length > max) fail();
  return value;
}
function integer(value: unknown, max: number): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 0 || value > max) fail();
  return value;
}
function bool(value: unknown): boolean {
  if (typeof value !== 'boolean') fail();
  return value;
}
function reasons(value: unknown): string[] {
  return array(value, 64).map((v) => {
    const s = text(v, 128);
    if (!/^[a-z0-9-]+$/.test(s)) fail();
    return s;
  });
}
function guid(value: unknown): string {
  const s = text(value, 32);
  if (!/^[a-f0-9]{32}$/.test(s)) fail();
  return s;
}
function date(value: unknown): string {
  const s = text(value, 10);
  if (
    !/^\d{4}-\d{2}-\d{2}$/.test(s) ||
    !Number.isFinite(Date.parse(`${s}T00:00:00Z`)) ||
    new Date(`${s}T00:00:00Z`).toISOString().slice(0, 10) !== s
  )
    fail();
  return s;
}
function time(value: unknown, scheduled = false): string {
  const s = text(value, 40);
  if (
    !(
      scheduled
        ? /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:Z|[+-]\d{2}:\d{2})$/
        : /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/
    ).test(s) ||
    !Number.isFinite(Date.parse(s))
  )
    fail();
  date(s.slice(0, 10));
  if (s[11] > '2' || Number(s.slice(11, 13)) > 23 || Number(s.slice(14, 16)) > 59 || Number(s.slice(17, 19)) > 59)
    fail();
  const m = /([+-])(\d{2}):(\d{2})$/.exec(s);
  if (m && (Number(m[2]) > 14 || Number(m[3]) > 59 || (m[2] === '14' && m[3] !== '00'))) fail();
  return s;
}
function ref(value: unknown): RailRef {
  const r = object(value, ['source', 'code']);
  const code = text(r['code'], 128);
  if (r['source'] !== 'yandex-rasp' || !/^s\d+$/.test(code)) fail();
  return { source: 'yandex-rasp', code };
}
function sameRef(a: RailRef, b: RailRef): boolean {
  return a.source === b.source && a.code === b.code;
}
function attribution(value: unknown, profile: RailProfile): RailAttribution {
  const r = object(value, ['frameUrl', 'text', 'url', 'width', 'height']);
  if (
    r['frameUrl'] !==
      (profile === 'fictional'
        ? '/rail-fictional-banner.html'
        : 'https://yandex.st/rasp/media/apicc/copyright_vert_mono.html') ||
    r['url'] !== 'https://rasp.yandex.ru/' ||
    r['width'] !== 240 ||
    r['height'] !== 130
  )
    fail();
  return { frameUrl: r['frameUrl'] as string, text: text(r['text']), url: r['url'] as string, width: 240, height: 130 };
}
function station(value: unknown): RailStation {
  const r = object(value, ['reference', 'name', 'settlement', 'region']);
  if (!text(r['name']).trim()) fail();
  return {
    reference: ref(r['reference']),
    name: text(r['name']),
    settlement: nullableText(r['settlement']),
    region: nullableText(r['region']),
  };
}
function price(value: unknown, receivedAt: string): RailPrice {
  const r = object(value, ['state', 'amount', 'currency', 'reason', 'receivedAt']);
  const state = member(r['state'], ['reported', 'not-provided', 'unavailable']);
  const reason = nullableText(r['reason']);
  if (time(r['receivedAt']) !== receivedAt) fail();
  if (state !== 'reported') {
    if (r['amount'] !== null || r['currency'] !== null || reason === null) fail();
    return { state, amount: null, currency: null, reason, receivedAt };
  }
  const amount = text(r['amount'], 32);
  if (
    !/^(?:0|[1-9]\d*)\.\d{2}$/.test(amount) ||
    BigInt(amount.replace('.', '')) > 79228162514264337593543950335n ||
    reason !== null
  )
    fail();
  return { state, amount, currency: member(r['currency'], ['RUB', 'USD', 'EUR']), reason: null, receivedAt };
}
function tickets(value: unknown, receivedAt: string): RailTickets {
  const r = object(value, [
    'electronicTicket',
    'electronicTicketReason',
    'faresState',
    'fares',
    'reasons',
    'seatAvailability',
    'operationalUpdates',
    'damaged',
  ]);
  const electronicTicket = member(r['electronicTicket'], ['true', 'false', 'unknown']);
  const electronicTicketReason = nullableText(r['electronicTicketReason']);
  if (
    (electronicTicket === 'unknown') !== (electronicTicketReason !== null) ||
    r['seatAvailability'] !== 'unknown' ||
    r['operationalUpdates'] !== 'not-provided'
  )
    fail();
  const fares = array(r['fares'], 32).map((v) => {
    const f = object(v, ['name', 'price']);
    return { name: nullableText(f['name']), price: price(f['price'], receivedAt) };
  });
  const faresState = member(r['faresState'], ['not-provided', 'reported', 'limited', 'unavailable']);
  const fareReasons = reasons(r['reasons']);
  if (
    (faresState === 'unavailable' && (fares.length !== 0 || fareReasons.length === 0)) ||
    (faresState === 'not-provided' && (fares.length !== 0 || fareReasons.length !== 0)) ||
    (faresState === 'reported' &&
      (fares.length === 0 || fareReasons.length !== 0 || fares.some((f) => f.price.state === 'unavailable'))) ||
    (faresState === 'limited' && fareReasons.length === 0)
  )
    fail();
  const damaged =
    electronicTicketReason === 'invalid-indicator' ||
    faresState === 'limited' ||
    faresState === 'unavailable' ||
    fares.some((f) => f.price.reason === 'incomplete-price');
  if (bool(r['damaged']) !== damaged) fail();
  return {
    electronicTicket,
    electronicTicketReason,
    faresState,
    fares,
    reasons: reasons(r['reasons']),
    seatAvailability: 'unknown',
    operationalUpdates: 'not-provided',
    damaged: bool(r['damaged']),
  };
}
function request(value: unknown): RailRequest {
  const r = object(value, ['from', 'to', 'catalogueGeneration', 'departureDate', 'transport']);
  const result = {
    from: ref(r['from']),
    to: ref(r['to']),
    catalogueGeneration: guid(r['catalogueGeneration']),
    departureDate: date(r['departureDate']),
    transport: member(r['transport'], ['train', 'suburban', 'all']),
  };
  if (sameRef(result.from, result.to)) fail();
  return result;
}
function sameRequest(a: RailRequest, b: RailRequest): boolean {
  return (
    sameRef(a.from, b.from) &&
    sameRef(a.to, b.to) &&
    a.catalogueGeneration === b.catalogueGeneration &&
    a.departureDate === b.departureDate &&
    a.transport === b.transport
  );
}
function journey(value: unknown, expected: RailRequest): RailJourney {
  const r = object(value, [
    'key',
    'threadReference',
    'transport',
    'from',
    'to',
    'departure',
    'arrival',
    'durationSeconds',
    'sourceReportedDate',
    'number',
    'title',
    'carrier',
    'departurePlatform',
    'arrivalPlatform',
    'stops',
    'tickets',
    'receivedAt',
    'reasons',
    'source',
    'departureTimeZone',
    'arrivalTimeZone',
    'sourceUpdatedAt',
  ]);
  const receivedAt = time(r['receivedAt']);
  const departure = time(r['departure'], true);
  const arrival = time(r['arrival'], true);
  const transport = member(r['transport'], ['train', 'suburban']);
  const from = ref(r['from']);
  const to = ref(r['to']);
  const durationSeconds = integer(r['durationSeconds'], Number.MAX_SAFE_INTEGER);
  const threadReference = text(r['threadReference'], 400);
  if (
    threadReference.length > 100 ||
    hasControl(threadReference) ||
    !threadReference ||
    !sameRef(from, expected.from) ||
    !sameRef(to, expected.to) ||
    departure.slice(0, 10) !== expected.departureDate ||
    (Date.parse(arrival) - Date.parse(departure)) / 1000 !== durationSeconds ||
    durationSeconds <= 0 ||
    r['source'] !== 'yandex-rasp' ||
    r['departureTimeZone'] !== null ||
    r['arrivalTimeZone'] !== null ||
    r['sourceUpdatedAt'] !== null
  )
    fail();
  return {
    key: text(r['key'], 1024),
    threadReference,
    transport,
    from,
    to,
    departure,
    arrival,
    durationSeconds,
    sourceReportedDate: r['sourceReportedDate'] === null ? null : date(r['sourceReportedDate']),
    number: nullableText(r['number']),
    title: nullableText(r['title']),
    carrier: nullableText(r['carrier']),
    departurePlatform: nullableText(r['departurePlatform']),
    arrivalPlatform: nullableText(r['arrivalPlatform']),
    stops: nullableText(r['stops']),
    tickets: tickets(r['tickets'], receivedAt),
    receivedAt,
    reasons: reasons(r['reasons']),
    source: 'yandex-rasp',
    departureTimeZone: null,
    arrivalTimeZone: null,
    sourceUpdatedAt: null,
  };
}
function bounded(value: unknown): void {
  if (new TextEncoder().encode(JSON.stringify(value)).length > 4 * 1024 * 1024) fail();
}
function freshness(
  received: unknown,
  expires: unknown,
  usable: boolean,
  now: number,
): { receivedAt: string | null; expiresAt: string | null } {
  if (!usable) {
    if (received !== null || expires !== null) fail();
    return { receivedAt: null, expiresAt: null };
  }
  const receivedAt = time(received);
  const expiresAt = time(expires);
  if (
    Date.parse(expiresAt) <= now ||
    Date.parse(expiresAt) <= Date.parse(receivedAt) ||
    Date.parse(receivedAt) > now + 1000
  )
    fail();
  return { receivedAt, expiresAt };
}
export function decodeRailSuggestions(value: unknown, now = Date.now()): RailSuggestions {
  bounded(value);
  const r = object(value, [
    'kind',
    'reasons',
    'suggestions',
    'hasMore',
    'catalogueGeneration',
    'source',
    'profile',
    'profileEpoch',
    'receivedAt',
    'expiresAt',
    'attribution',
  ]);
  const kind = member(r['kind'], ['complete', 'limited', 'skipped', 'failed']);
  const usable = kind === 'complete' || kind === 'limited';
  const profile = member(r['profile'], ['real', 'fictional']);
  if (r['source'] !== 'yandex-rasp') fail();
  const suggestions = array(r['suggestions'], 20).map(station);
  const hasMore = bool(r['hasMore']);
  if (
    (!usable &&
      (suggestions.length > 0 || hasMore || r['catalogueGeneration'] !== null || r['attribution'] !== null)) ||
    (hasMore && kind !== 'limited')
  )
    fail();
  return {
    kind,
    reasons: reasons(r['reasons']),
    suggestions,
    hasMore,
    catalogueGeneration: usable ? guid(r['catalogueGeneration']) : null,
    source: 'yandex-rasp',
    profile,
    profileEpoch: guid(r['profileEpoch']),
    ...freshness(r['receivedAt'], r['expiresAt'], usable, now),
    attribution: usable ? attribution(r['attribution'], profile) : null,
  };
}
export function decodeRailSearch(
  value: unknown,
  expected: RailRequest,
  now = Date.now(),
  authority?: RailExpectedAuthority,
): RailSearchResult {
  bounded(value);
  const r = object(value, [
    'kind',
    'reasons',
    'request',
    'source',
    'profile',
    'profileEpoch',
    'modes',
    'items',
    'normalizedCount',
    'receivedAt',
    'expiresAt',
    'completedAt',
    'attribution',
  ]);
  const admitted = request(r['request']);
  if (!sameRequest(admitted, expected) || r['source'] !== 'yandex-rasp') fail();
  const kind = member(r['kind'], ['complete', 'limited', 'skipped', 'failed']);
  const profile = member(r['profile'], ['real', 'fictional']);
  const profileEpoch = guid(r['profileEpoch']);
  if (authority && (authority.profile !== profile || authority.profileEpoch !== profileEpoch))
    throw new RailProfileNoticeError();
  const requested: RailMode[] = expected.transport === 'all' ? ['train', 'suburban'] : [expected.transport];
  const modes = array(r['modes'], 2).map((v) => {
    const m = object(v, ['transport', 'kind', 'reasons', 'originalCount', 'normalizedCount', 'receivedAt']);
    const transport = member(m['transport'], ['train', 'suburban']);
    const modeKind: RailKind = member(m['kind'], ['complete', 'limited', 'skipped', 'failed']);
    const originalCount = integer(m['originalCount'], 50);
    const normalizedCount = integer(m['normalizedCount'], 50);
    const usable = modeKind === 'complete' || modeKind === 'limited';
    if (
      normalizedCount > originalCount ||
      (!usable && (originalCount !== 0 || normalizedCount !== 0 || m['receivedAt'] !== null))
    )
      fail();
    return {
      transport,
      kind: modeKind,
      reasons: reasons(m['reasons']),
      originalCount,
      normalizedCount,
      receivedAt: usable ? time(m['receivedAt']) : null,
    };
  });
  if (
    modes.length !== requested.length ||
    new Set(modes.map((m) => m.transport)).size !== requested.length ||
    requested.some((m) => !modes.some((v) => v.transport === m))
  )
    fail();
  const items = array(r['items'], 100).map((v) => journey(v, expected));
  if (integer(r['normalizedCount'], 100) !== items.length || new Set(items.map((x) => x.key)).size !== items.length)
    fail();
  if (
    new Set(
      items.map((x) =>
        [
          x.source,
          x.threadReference,
          x.transport,
          x.from.code,
          x.to.code,
          Date.parse(x.departure),
          Date.parse(x.arrival),
        ].join('|'),
      ),
    ).size !== items.length
  )
    fail();
  for (const mode of modes) {
    const rows = items.filter((x) => x.transport === mode.transport);
    if (rows.length !== mode.normalizedCount || rows.some((x) => x.receivedAt !== mode.receivedAt)) fail();
  }
  if (items.some((x) => !requested.includes(x.transport))) fail();
  const usable = modes.filter((x) => x.kind === 'complete' || x.kind === 'limited');
  const computed =
    usable.length > 0
      ? modes.every((x) => x.kind === 'complete')
        ? 'complete'
        : 'limited'
      : modes.some((x) => x.kind === 'failed')
        ? 'failed'
        : 'skipped';
  if (computed !== kind) fail();
  const fresh = freshness(r['receivedAt'], r['expiresAt'], usable.length > 0, now);
  if (authority?.expiresAt && fresh.expiresAt && Date.parse(fresh.expiresAt) > Date.parse(authority.expiresAt)) fail();
  if (
    usable.length > 0 &&
    Date.parse(fresh.expiresAt!) - Date.parse(fresh.receivedAt!) >
      (items.length === 0 || items.some((x) => x.tickets.damaged) ? 30000 : 300000)
  )
    fail();
  if (usable.length > 0 && Date.parse(fresh.receivedAt!) !== Math.min(...usable.map((x) => Date.parse(x.receivedAt!))))
    fail();
  return {
    kind,
    reasons: reasons(r['reasons']),
    request: admitted,
    source: 'yandex-rasp',
    profile,
    profileEpoch: guid(r['profileEpoch']),
    modes,
    items,
    normalizedCount: items.length,
    ...fresh,
    completedAt: time(r['completedAt']),
    attribution: usable.length > 0 ? attribution(r['attribution'], profile) : r['attribution'] === null ? null : fail(),
  };
}
