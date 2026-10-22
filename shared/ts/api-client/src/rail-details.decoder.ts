import { railDecoding as d, type RailExpectedAuthority, RailProfileNoticeError } from './rail.decoder';
import type {
  RailJourneyDetailsRequest,
  RailJourneyDetailsResult,
  RailKind,
  RailRouteStop,
  RailStopFact,
} from './rail.types';

const kinds = ['complete', 'limited', 'skipped', 'failed'] as const;
const usable = (kind: RailKind) => kind === 'complete' || kind === 'limited';
const offset = (time: string) => (time.endsWith('Z') || /[+-]00:00$/.test(time) ? '+00:00' : time.slice(-6));
const sameTime = (a: string, b: string) => Date.parse(a) === Date.parse(b) && offset(a) === offset(b);
function codes(value: unknown) {
  const codes = d.reasons(value);
  if (new Set(codes).size !== codes.length) d.fail();
  return codes;
}
function selection(value: unknown): RailJourneyDetailsRequest['selection'] {
  const r = d.object(value, ['key', 'transport', 'departure', 'arrival']);
  const key = d.text(r['key']);
  if (!key.trim() || d.hasControl(key)) d.fail();
  const departure = d.time(r['departure'], true);
  const arrival = d.time(r['arrival'], true);
  if (Date.parse(arrival) <= Date.parse(departure)) d.fail();
  return { key, transport: d.member(r['transport'], ['train', 'suburban']), departure, arrival };
}
function request(value: unknown): RailJourneyDetailsRequest {
  const r = d.object(value, ['search', 'expectedProfileEpoch', 'selection']);
  const result = {
    search: d.request(r['search']),
    expectedProfileEpoch: d.guid(r['expectedProfileEpoch']),
    selection: selection(r['selection']),
  };
  if (
    (result.search.transport !== 'all' && result.search.transport !== result.selection.transport) ||
    result.selection.departure.slice(0, 10) !== result.search.departureDate
  )
    d.fail();
  return result;
}
function fact<T>(value: unknown, parse: (value: unknown) => T, allowed: string[]): RailStopFact<T> {
  const r = d.object(value, ['state', 'value', 'reason']);
  const state = d.member(r['state'], ['reported', 'not-provided', 'unavailable']);
  if (state === 'reported') {
    if (r['reason'] !== null) d.fail();
    return { state, value: parse(r['value']), reason: null };
  }
  if (r['value'] !== null) d.fail();
  const reason = d.member(r['reason'], allowed);
  if (state === 'unavailable' ? !reason.startsWith('invalid-') : reason.startsWith('invalid-')) d.fail();
  return { state, value: null, reason };
}
function stop(value: unknown, index: number, count: number): RailRouteStop {
  const r = d.object(value, ['sequence', 'station', 'arrival', 'departure', 'dwellSeconds', 'platform', 'reasons']);
  if (d.integer(r['sequence'], 199) !== index) d.fail();
  const station = d.object(r['station'], ['source', 'code', 'name']);
  const reference = d.ref({ source: station['source'], code: station['code'] });
  const name = d.text(station['name']);
  if (!name.trim()) d.fail();
  const arrival = fact(r['arrival'], (value) => d.time(value, true), [
    'terminal-not-provided',
    'missing-time',
    'invalid-time',
  ]);
  const departure = fact(r['departure'], (value) => d.time(value, true), [
    'terminal-not-provided',
    'missing-time',
    'invalid-time',
  ]);
  if (
    (arrival.reason === 'terminal-not-provided' && index !== 0) ||
    (departure.reason === 'terminal-not-provided' && index !== count - 1)
  )
    d.fail();
  if (
    (index === 0 && arrival.state === 'not-provided' && arrival.reason !== 'terminal-not-provided') ||
    (index === count - 1 && departure.state === 'not-provided' && departure.reason !== 'terminal-not-provided')
  )
    d.fail();
  const dwellSeconds = fact(r['dwellSeconds'], (value) => d.integer(value, Number.MAX_SAFE_INTEGER), [
    'missing-dwell',
    'invalid-dwell',
  ]);
  const platform = fact(
    r['platform'],
    (value) => {
      const s = d.text(value);
      if (s.length === 0) d.fail();
      return s;
    },
    ['missing-platform', 'invalid-platform'],
  );
  if (
    dwellSeconds.value !== null &&
    arrival.value !== null &&
    departure.value !== null &&
    dwellSeconds.value * 1000 !== Date.parse(departure.value) - Date.parse(arrival.value)
  )
    d.fail();
  const reasons = codes(r['reasons']);
  const expected = [arrival, departure, dwellSeconds, platform]
    .filter((f) => f.reason?.startsWith('invalid-') || f.reason === 'missing-time')
    .map((f) => f.reason!);
  if (new Set(expected).size !== reasons.length || reasons.some((reason) => !expected.includes(reason))) d.fail();
  return { sequence: index, station: { ...reference, name }, arrival, departure, dwellSeconds, platform, reasons };
}
export function decodeRailDetails(
  value: unknown,
  expected: RailJourneyDetailsRequest,
  now: number,
  authority: RailExpectedAuthority,
): RailJourneyDetailsResult {
  // An old profile is a clear-source notice even if the old payload's other fields are damaged.
  if (value && typeof value === 'object' && !Array.isArray(value)) {
    const minimal = value as Record<string, unknown>;
    if (
      minimal['source'] === 'yandex-rasp' &&
      (minimal['profile'] === 'real' || minimal['profile'] === 'fictional') &&
      typeof minimal['profileEpoch'] === 'string' &&
      /^[a-f0-9]{32}$/.test(minimal['profileEpoch']) &&
      (minimal['profile'] !== authority.profile || minimal['profileEpoch'] !== authority.profileEpoch)
    )
      throw new RailProfileNoticeError();
  }
  const r = d.object(value, [
    'kind',
    'reasons',
    'stage',
    'request',
    'source',
    'profile',
    'profileEpoch',
    'verification',
    'routeReceivedAt',
    'expiresAt',
    'completedAt',
    'stops',
    'boardingIndex',
    'alightingIndex',
    'attribution',
  ]);
  if (new TextEncoder().encode(JSON.stringify(value)).length > 512 * 1024 || r['source'] !== 'yandex-rasp') d.fail();
  const admitted = request(r['request']);
  const selected = admitted.selection;
  if (
    !d.sameRequest(admitted.search, expected.search) ||
    admitted.expectedProfileEpoch !== expected.expectedProfileEpoch ||
    selected.key !== expected.selection.key ||
    selected.transport !== expected.selection.transport ||
    !sameTime(selected.departure, expected.selection.departure) ||
    !sameTime(selected.arrival, expected.selection.arrival)
  )
    d.fail();
  const kind = d.member(r['kind'], kinds);
  const reasons = codes(r['reasons']);
  const stage = d.member(r['stage'], ['admission', 'verification', 'route']);
  const profile = d.member(r['profile'], ['real', 'fictional']);
  const profileEpoch = d.guid(r['profileEpoch']);
  if (profile !== authority.profile || profileEpoch !== authority.profileEpoch || reasons.includes('profile-changed'))
    throw new RailProfileNoticeError();
  if (usable(kind) && admitted.expectedProfileEpoch !== profileEpoch) d.fail();
  const completedAt = d.time(r['completedAt']);
  if (Date.parse(completedAt) > now + 1000) d.fail();
  const routeReceivedAt = r['routeReceivedAt'] === null ? null : d.time(r['routeReceivedAt']);
  if (routeReceivedAt && Date.parse(routeReceivedAt) > Date.parse(completedAt)) d.fail();
  let verification: RailJourneyDetailsResult['verification'] = null;
  if (r['verification'] !== null) {
    const v = d.object(r['verification'], ['transport', 'kind', 'reasons', 'receivedAt']);
    verification = {
      transport: d.member(v['transport'], ['train', 'suburban']),
      kind: d.member(v['kind'], kinds),
      reasons: codes(v['reasons']),
      receivedAt: v['receivedAt'] === null ? null : d.time(v['receivedAt']),
    };
    if (
      verification.transport !== selected.transport ||
      (usable(verification.kind) && !verification.receivedAt) ||
      (!usable(verification.kind) && verification.receivedAt !== null) ||
      (verification.kind === 'complete' && verification.reasons.length !== 0) ||
      (verification.kind !== 'complete' && verification.reasons.length === 0) ||
      (verification.receivedAt && Date.parse(verification.receivedAt) > Date.parse(routeReceivedAt ?? completedAt))
    )
      d.fail();
  }
  if (stage === 'admission') {
    if (
      kind !== 'skipped' ||
      verification !== null ||
      routeReceivedAt !== null ||
      reasons.length === 0 ||
      reasons.some((reason) => !['source-disabled', 'details-disabled'].includes(reason))
    )
      d.fail();
  } else if (
    !verification ||
    (stage === 'verification' && (usable(kind) || routeReceivedAt !== null)) ||
    (stage === 'route' && !usable(verification.kind))
  )
    d.fail();
  const rows = d.array(r['stops'], 200);
  const stops = rows.map((s, i) => stop(s, i, rows.length));
  if (!usable(kind)) {
    if (
      stops.length ||
      r['boardingIndex'] !== null ||
      r['alightingIndex'] !== null ||
      r['expiresAt'] !== null ||
      r['attribution'] !== null ||
      reasons.length === 0
    )
      d.fail();
    if (stage === 'verification' && verification && !usable(verification.kind) && kind !== verification.kind) d.fail();
    return {
      kind,
      reasons,
      stage,
      request: admitted,
      source: 'yandex-rasp',
      profile,
      profileEpoch,
      verification,
      routeReceivedAt,
      expiresAt: null,
      completedAt,
      stops,
      boardingIndex: null,
      alightingIndex: null,
      attribution: null,
    };
  }
  if (stage !== 'route' || !verification || !routeReceivedAt || stops.length < 2) d.fail();
  const boardingIndex = d.integer(r['boardingIndex'], stops.length - 1);
  const alightingIndex = d.integer(r['alightingIndex'], stops.length - 1);
  if (boardingIndex >= alightingIndex) d.fail();
  let previous = Number.NEGATIVE_INFINITY;
  const pairs: [number, number][] = [];
  for (let i = 0; i < stops.length; i++) {
    const s = stops[i];
    for (const f of [s.arrival, s.departure])
      if (f.value !== null) {
        const instant = Date.parse(f.value);
        if (instant < previous) d.fail();
        previous = instant;
      }
    if (
      d.sameRef(s.station, admitted.search.from) &&
      s.departure.value &&
      sameTime(s.departure.value, selected.departure)
    )
      for (let j = i + 1; j < stops.length; j++)
        if (
          d.sameRef(stops[j].station, admitted.search.to) &&
          stops[j].arrival.value &&
          sameTime(stops[j].arrival.value!, selected.arrival)
        )
          pairs.push([i, j]);
  }
  if (pairs.length !== 1 || pairs[0][0] !== boardingIndex || pairs[0][1] !== alightingIndex) d.fail();
  const defects = [...new Set(stops.flatMap((s) => s.reasons))];
  if (
    kind === 'complete'
      ? reasons.length !== 0 || defects.length !== 0
      : reasons.length === 0 ||
        reasons.some((reason) => reason !== 'echo-date-unavailable' && !defects.includes(reason)) ||
        defects.some((reason) => !reasons.includes(reason))
  )
    d.fail();
  const expiresAt = d.time(r['expiresAt']);
  const expiry = Date.parse(expiresAt);
  if (
    expiry <= now ||
    Date.parse(completedAt) >= expiry ||
    expiry > Date.parse(routeReceivedAt!) + 60000 ||
    (authority.expiresAt !== null && expiry > Date.parse(authority.expiresAt))
  )
    d.fail();
  return {
    kind,
    reasons,
    stage,
    request: admitted,
    source: 'yandex-rasp',
    profile,
    profileEpoch,
    verification,
    routeReceivedAt,
    expiresAt,
    completedAt,
    stops,
    boardingIndex,
    alightingIndex,
    attribution: d.attribution(r['attribution'], profile),
  };
}
