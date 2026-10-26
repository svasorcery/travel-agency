import { tripDate, tripRevision } from './trips.decoder';
import { tripItineraryReleases } from './trips-itinerary.releases';
import type {
  TripItineraryAvailability,
  TripItineraryAvailabilityResult,
  TripItineraryCandidate,
  TripItineraryComputed,
  TripItineraryErrorKind,
  TripItineraryProblem,
} from './trips-itinerary.types';
export class TripsItineraryContractError extends Error {
  constructor() {
    super('Invalid Trips itinerary contract.');
    this.name = 'TripsItineraryContractError';
  }
}
const fail = (): never => {
  throw new TripsItineraryContractError();
};
function requireValue(ok: unknown): asserts ok {
  if (!ok) fail();
}
export type TripItineraryRaw = ArrayBuffer | Uint8Array | string;
const surrogate = /[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/u;
// Small bounded wire parser: validates decoded keys before constructing a null-prototype object.
export function parseTripItineraryJson(raw: TripItineraryRaw, maxBytes = 131072, maxDepth = 16): unknown {
  let text: string;
  try {
    if (typeof raw === 'string') {
      requireValue(!surrogate.test(raw) && new TextEncoder().encode(raw).byteLength <= maxBytes);
      text = raw;
    } else {
      const bytes = raw instanceof Uint8Array ? raw : new Uint8Array(raw);
      requireValue(bytes.byteLength <= maxBytes && !(bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf));
      text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
    }
    let i = 0;
    const ws = () => {
      while (i < text.length && [9, 10, 13, 32].includes(text.charCodeAt(i))) i++;
    };
    const string = (): string => {
      const start = i++;
      while (i < text.length) {
        const char = text[i++];
        if (char === '"') {
          const result: unknown = JSON.parse(text.slice(start, i));
          requireValue(typeof result === 'string' && !surrogate.test(result));
          return result;
        }
        if (char === '\\') {
          requireValue(i < text.length);
          i++;
        } else requireValue(char.charCodeAt(0) >= 32);
      }
      return fail();
    };
    const value = (depth: number): unknown => {
      ws();
      const char = text[i];
      if (char === '"') return string();
      if (char === '{' || char === '[') {
        requireValue(depth < maxDepth);
        i++;
        ws();
        if (char === '[') {
          const result: unknown[] = [];
          if (text[i] === ']') {
            i++;
            return result;
          }
          while (true) {
            result.push(value(depth + 1));
            ws();
            if (text[i] === ']') {
              i++;
              return result;
            }
            requireValue(text[i++] === ',');
          }
        }
        const result: Record<string, unknown> = Object.create(null);
        if (text[i] === '}') {
          i++;
          return result;
        }
        while (true) {
          ws();
          requireValue(text[i] === '"');
          const key = string();
          requireValue(!Object.hasOwn(result, key));
          ws();
          requireValue(text[i++] === ':');
          result[key] = value(depth + 1);
          ws();
          if (text[i] === '}') {
            i++;
            return result;
          }
          requireValue(text[i++] === ',');
        }
      }
      const token = /^(?:true|false|null|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/.exec(text.slice(i));
      requireValue(token);
      i += token[0].length;
      const result: unknown = JSON.parse(token[0]);
      requireValue(typeof result !== 'number' || Number.isFinite(result));
      return result;
    };
    const result = value(0);
    ws();
    requireValue(i === text.length);
    return result;
  } catch {
    return fail();
  }
}
function object(value: unknown, keys: string, optional = ''): Record<string, unknown> {
  requireValue(value !== null && typeof value === 'object' && !Array.isArray(value));
  const record = value as Record<string, unknown>,
    required = keys.split(' ').filter((key) => key.length > 0),
    allowed = [...required, ...optional.split(' ').filter((key) => key.length > 0)];
  requireValue(
    required.every((key) => Object.hasOwn(record, key)) && Object.keys(record).every((key) => allowed.includes(key)),
  );
  return record;
}
function text(v: unknown, max = 64): string {
  requireValue(
    typeof v === 'string' &&
      v.length > 0 &&
      [...v].length <= max &&
      !surrogate.test(v) &&
      [...v].every((char) => {
        const code = char.codePointAt(0) ?? 0;
        return code >= 32 && (code < 127 || code > 159);
      }),
  );
  return v;
}
function pattern(v: unknown, regex: RegExp): string {
  const s = text(v, 256);
  requireValue(regex.test(s));
  return s;
}
const id = (v: unknown) => pattern(v, /^[a-z][a-z0-9-]{0,63}$/);
const version = (v: unknown) => pattern(v, /^[a-z0-9.-]{1,64}$/);
const hash = (v: unknown) => pattern(v, /^[a-f0-9]{64}$/);
function utc(v: unknown): string {
  const s = pattern(v, /^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d\.\d{7}Z$/);
  tripDate(s.slice(0, 10));
  return s;
}
// Canonical UTC keeps all seven fractional digits; Date.parse alone discards four.
const ticks = (value: string): bigint =>
  BigInt(Date.parse(value.slice(0, 19) + 'Z')) * 10000n + BigInt(value.slice(20, 27));
function integer(v: unknown, min: number, max: number): number {
  requireValue(typeof v === 'number' && Number.isInteger(v) && v >= min && v <= max);
  return v;
}
function list(v: unknown, max: number, min = 0): unknown[] {
  requireValue(Array.isArray(v) && v.length >= min && v.length <= max);
  return v;
}
function enumValue<T extends string>(v: unknown, values: readonly T[]): T {
  requireValue(typeof v === 'string' && values.includes(v as T));
  return v as T;
}
function strings(v: unknown, allowed?: readonly string[]): string[] {
  const a = list(v, 16).map((x) => text(x));
  requireValue(new Set(a).size === a.length && (!allowed || a.every((x) => allowed.includes(x))));
  return a;
}
function equalSet(a: readonly string[], b: readonly string[]) {
  return a.length === b.length && a.every((v) => b.includes(v));
}
function url(v: unknown): URL {
  const s = text(v, 256);
  requireValue(s.length <= 256 && /^https:\/\//.test(s));
  const u = new URL(s);
  requireValue(u.protocol === 'https:' && !u.username && !u.password && !u.hash);
  return u;
}
const attribution =
  '© OpenStreetMap contributors; ODbL: https://opendatacommons.org/licenses/odbl/1-0/; https://www.openstreetmap.org/copyright.';
const assumptions =
  '75/45 metres per minute plus one modelled minute per mapped crossing: author scenarios, not observations, confidence or a guaranteed upper bound.';
const interests = ['architecture', 'culture', 'history', 'nature', 'food'];
function identity(r: Record<string, unknown>, protocol: 1 | 2) {
  const city = r['destinationId'];
  requireValue(
    r['candidateVersion'] === protocol &&
      (city === 'moscow' || (protocol === 2 && city === 'saint-petersburg')) &&
      r['dataMode'] === 'osm-modelled-demo' &&
      r['humanQuality'] === 'not-evaluated',
  );
  id(r['datasetId']);
  version(r['datasetVersion']);
  hash(r['datasetHash']);
  requireValue(r['policyVersion'] === 'bounded-beam-v1');
  utc(r['releaseValidUntil']);
  requireValue(city === 'moscow' || city === 'saint-petersburg');
  const release = tripItineraryReleases[city];
  if (protocol === 2)
    requireValue(
      r['datasetId'] === release.datasetId &&
        r['datasetVersion'] === release.version &&
        r['datasetHash'] === release.hash,
    );
  return release;
}
export function decodeTripItineraryAvailability(raw: TripItineraryRaw): TripItineraryAvailability {
  return decodeAvailability(raw, 1) as TripItineraryAvailability;
}
export function decodeTripItineraryAvailabilityV2(raw: TripItineraryRaw): TripItineraryAvailabilityResult {
  return decodeAvailability(raw, 2);
}
function decodeAvailability(raw: TripItineraryRaw, protocol: 1 | 2): TripItineraryAvailabilityResult {
  try {
    const value = parseTripItineraryJson(raw, 2048, 4);
    const initial = value as Record<string, unknown> | null;
    if (protocol === 2 && initial && initial['status'] === 'unsupported') {
      const r = object(value, 'candidateVersion status destinationId reason');
      requireValue(
        r['candidateVersion'] === 2 && r['destinationId'] === 'kaliningrad' && r['reason'] === 'destinationUnsupported',
      );
      return r as TripItineraryAvailabilityResult;
    }
    const r = object(
      value,
      'candidateVersion status destinationId datasetId datasetVersion datasetHash policyVersion dataMode humanQuality releaseValidUntil',
    );
    identity(r, protocol);
    requireValue(r['status'] === 'ready');
    return r as TripItineraryAvailability;
  } catch {
    return fail();
  }
}
export function decodeTripItineraryCandidate(raw: TripItineraryRaw): TripItineraryCandidate {
  return decodeCandidate(raw, 1);
}
export function decodeTripItineraryCandidateV2(raw: TripItineraryRaw): TripItineraryCandidate {
  return decodeCandidate(raw, 2);
}
function decodeCandidate(raw: TripItineraryRaw, protocol: 1 | 2): TripItineraryCandidate {
  try {
    const value = parseTripItineraryJson(raw);
    const initial = value as Record<string, unknown> | null;
    requireValue(initial && typeof initial === 'object');
    if (initial['outcome'] === 'unsupported') {
      const r = object(
        value,
        'candidateVersion outcome baseRevision destinationId timeZone catalogueVersion policyVersion generatedAt reason',
      );
      requireValue(r['candidateVersion'] === protocol && r['policyVersion'] === 'bounded-beam-v1');
      tripRevision(r['baseRevision']);
      const city = enumValue(r['destinationId'], ['moscow', 'saint-petersburg', 'kaliningrad']);
      requireValue(
        r['timeZone'] === (city === 'kaliningrad' ? 'Europe/Kaliningrad' : 'Europe/Moscow') &&
          r['catalogueVersion'] === '1',
      );
      utc(r['generatedAt']);
      enumValue(r['reason'], [
        'destinationUnsupported',
        'dateRangeUnsupported',
        'historicalDatesUnsupported',
        'horizonUnsupported',
      ]);
      requireValue(
        (r['reason'] === 'destinationUnsupported') ===
          (city !== 'moscow' && !(protocol === 2 && city === 'saint-petersburg')),
      );
      return r as unknown as TripItineraryCandidate;
    }
    const r = object(
      value,
      'candidateVersion outcome baseRevision destinationId timeZone catalogueVersion datasetId datasetVersion datasetHash policyVersion generatedAt expiresAt releaseValidUntil dataMode humanQuality preparation modelSource attribution derivedDataOffer coverage coveredInterests unmetInterests reasons exclusions uncertainties searchLimited expansionCount days evidence',
    );
    const releaseIdentity = identity(r, protocol);
    tripRevision(r['baseRevision']);
    enumValue(r['outcome'], ['candidate', 'empty']);
    requireValue(r['timeZone'] === 'Europe/Moscow' && r['catalogueVersion'] === '1');
    const generated = utc(r['generatedAt']),
      expires = utc(r['expiresAt']),
      release = utc(r['releaseValidUntil']);
    const expiryTicks = ticks(generated) + 9000000000n;
    requireValue(
      generated < expires && ticks(expires) === (expiryTicks < ticks(release) ? expiryTicks : ticks(release)),
    );
    const p = object(r['preparation'], 'preparedBy humanCurator humanReviewer calibrationStatus buildAsOfUtc');
    id(p['preparedBy']);
    requireValue(
      p['humanCurator'] === null && p['humanReviewer'] === null && p['calibrationStatus'] === 'not-evaluated',
    );
    const build = utc(p['buildAsOfUtc']);
    requireValue(build <= generated);
    const m = object(
      r['modelSource'],
      'sourceUrl inputSha256 publicationInputSha256 snapshotPublishedAtUtc downloadedAtUtc buildAsOfUtc profileVersion pairSelectorVersion modelVersion validUntil assumptions calibrationStatus',
    );
    url(m['sourceUrl']);
    hash(m['inputSha256']);
    hash(m['publicationInputSha256']);
    requireValue(
      m['inputSha256'] === m['publicationInputSha256'] &&
        m['profileVersion'] === releaseIdentity.profile &&
        (protocol === 1 || m['sourceUrl'] === releaseIdentity.sourceUrl) &&
        m['pairSelectorVersion'] === 'pair-selector-v1' &&
        m['modelVersion'] === 'model-v1' &&
        m['calibrationStatus'] === 'not-evaluated' &&
        m['assumptions'] === assumptions,
    );
    requireValue(
      utc(m['snapshotPublishedAtUtc']) <= utc(m['downloadedAtUtc']) &&
        utc(m['downloadedAtUtc']) <= build &&
        utc(m['buildAsOfUtc']) === build &&
        generated < utc(m['validUntil']) &&
        release <= utc(m['validUntil']) &&
        ticks(utc(m['validUntil'])) - ticks(utc(m['snapshotPublishedAtUtc'])) === 25920000000000n,
    );
    requireValue(r['attribution'] === attribution && r['derivedDataOffer'] === releaseIdentity.offer);
    const coverage = enumValue(r['coverage'], ['filled', 'partial']);
    const covered = strings(r['coveredInterests'], interests),
      unmet = strings(r['unmetInterests'], interests);
    requireValue(!covered.includes('food') && !covered.some((x) => unmet.includes(x)));
    const reasons = strings(r['reasons'], [
      'insufficientEligibleStops',
      'unmetInterests',
      'noEligibleStops',
      'searchLimited',
    ]);
    const exclusions = strings(r['exclusions'], [
      'arrivalScheduleNotPlanned',
      'indoorReservationsNotPlanned',
      'interdayTransfersNotPlanned',
      'totalCostUnknown',
      'transferNotPlanned',
    ]);
    requireValue(
      ['arrivalScheduleNotPlanned', 'indoorReservationsNotPlanned', 'interdayTransfersNotPlanned'].every((x) =>
        exclusions.includes(x),
      ),
    );
    const uncertainties = strings(r['uncertainties'], [
      'calibrationNotEvaluated',
      'humanQualityNotEvaluated',
      'openingHoursUnknown',
      'walkingModelled',
    ]);
    requireValue(typeof r['searchLimited'] === 'boolean');
    integer(r['expansionCount'], 0, 20000);
    requireValue(!r['searchLimited'] || r['expansionCount'] === 20000);
    const evidence = new Map<string, Record<string, unknown>>();
    for (const v of list(r['evidence'], 48)) {
      const e = object(
        v,
        'id kind scopeId method rightsBasis rightsEvidence sourceLabel sourceUrl retrievedAtUtc processedAtUtc observedAt validUntil permittedFields',
      );
      const eid = id(e['id']);
      requireValue(!evidence.has(eid));
      evidence.set(eid, e);
      id(e['scopeId']);
      enumValue(e['kind'], ['identity-location', 'access', 'walking']);
      enumValue(e['method'], ['source-record', 'observed', 'modelled']);
      enumValue(e['rightsBasis'], ['cc0', 'original-observation', 'explicit-permission', 'odbl']);
      text(e['rightsEvidence'], 500);
      requireValue(text(e['sourceLabel'], 253) === url(e['sourceUrl']).hostname);
      requireValue(
        utc(e['retrievedAtUtc']) <= utc(e['processedAtUtc']) &&
          utc(e['processedAtUtc']) <= generated &&
          generated < utc(e['validUntil']),
      );
      const origin = e['method'] === 'observed' ? utc(e['observedAt']) : utc(e['retrievedAtUtc']);
      if (e['kind'] !== 'walking')
        requireValue(
          ticks(utc(e['validUntil'])) - ticks(origin) <=
            BigInt(e['kind'] === 'identity-location' ? 90 : 7) * 864000000000n,
        );
      const fields = strings(e['permittedFields'], [
        'identity',
        'coordinates',
        'opening-hours',
        'walking-duration',
        'restrictions',
      ]);
      if (e['kind'] === 'identity-location')
        requireValue(
          e['method'] === 'source-record' &&
            e['rightsBasis'] === 'cc0' &&
            e['observedAt'] === null &&
            release <= utc(e['validUntil']) &&
            equalSet(fields, ['identity', 'coordinates']),
        );
      if (e['kind'] === 'walking')
        requireValue(
          e['method'] === 'modelled' &&
            e['rightsBasis'] === 'odbl' &&
            e['observedAt'] === null &&
            equalSet(fields, ['walking-duration', 'restrictions']) &&
            e['sourceUrl'] === m['sourceUrl'] &&
            e['retrievedAtUtc'] === m['downloadedAtUtc'] &&
            e['processedAtUtc'] === build &&
            e['validUntil'] === m['validUntil'],
        );
      if (e['kind'] === 'access')
        requireValue(
          equalSet(fields, ['opening-hours']) &&
            (e['method'] === 'observed'
              ? e['rightsBasis'] === 'original-observation' && utc(e['observedAt']) <= utc(e['retrievedAtUtc'])
              : e['method'] === 'source-record' &&
                e['rightsBasis'] === 'explicit-permission' &&
                e['observedAt'] === null),
        );
    }
    const used = new Set<string>(),
      pois = new Set<string>(),
      uuids = new Set<string>(),
      edges = new Set<string>(),
      tags = new Set<string>();
    let blocks = 0,
      edgeCount = 0,
      factCount = 0,
      unknown = false,
      previousDate = '';
    const ref = (eid: unknown, kind: string, scope: string, date: string) => {
      const key = id(eid),
        e = evidence.get(key);
      requireValue(e && e['kind'] === kind && e['scopeId'] === scope);
      // Both supported destinations use the existing Europe/Moscow calendar semantics.
      const localProcessed = new Date(Date.parse(e['processedAtUtc'] as string) + 10800000).toISOString().slice(0, 10);
      requireValue(date >= localProcessed && (e['validUntil'] as string) >= date + 'T21:00:00.0000000Z');
      used.add(key);
      return e;
    };
    const days = list(r['days'], 3, 1);
    for (const v of days) {
      const d = object(v, 'date blocks edges'),
        date = tripDate(d['date']);
      requireValue(!previousDate || Date.parse(date) - Date.parse(previousDate) === 86400000);
      previousDate = date;
      const today = new Date(Date.parse(generated) + 10800000).toISOString().slice(0, 10);
      requireValue(date >= today && Date.parse(date) - Date.parse(today) <= 30 * 86400000);
      const bs = list(d['blocks'], 4),
        es = list(d['edges'], 48);
      requireValue(es.length === Math.max(0, bs.length - 1));
      blocks += bs.length;
      edgeCount += es.length;
      for (const value of bs) {
        const b = object(
          value,
          'poiId proposedBlockId title date startTime endTime estimate note dwellMinutes tags wikidataEntityId wikidataRevision latitude longitude facts openingIntervals',
        );
        const poi = id(b['poiId']),
          uuid = pattern(b['proposedBlockId'], /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/);
        requireValue(uuid !== '00000000-0000-0000-0000-000000000000' && !uuids.has(uuid) && !pois.has(poi));
        uuids.add(uuid);
        pois.add(poi);
        requireValue(b['date'] === date && b['startTime'] === null && b['endTime'] === null && b['estimate'] === null);
        text(b['title'], 100);
        text(b['note'], 500);
        requireValue(
          b['note'] ===
            `Historical proposal: ${r['datasetId']}/${r['datasetVersion']}; asOf=${generated.replace('Z', '+00:00')}; POI=${poi}. ${attribution}`,
        );
        integer(b['dwellMinutes'], 15, 120);
        for (const tag of strings(b['tags'], interests.slice(0, 4))) tags.add(tag);
        requireValue((b['tags'] as string[]).length > 0);
        const qid = pattern(b['wikidataEntityId'], /^Q[1-9]\d{0,18}$/),
          rev = tripRevision(b['wikidataRevision']);
        requireValue(
          typeof b['latitude'] === 'number' &&
            Number.isFinite(b['latitude']) &&
            Math.abs(b['latitude']) <= 90 &&
            typeof b['longitude'] === 'number' &&
            Number.isFinite(b['longitude']) &&
            Math.abs(b['longitude']) <= 180,
        );
        if (protocol === 2) {
          const [south, west, north, east] = releaseIdentity.bounds;
          requireValue(
            (b['latitude'] as number) >= south &&
              (b['latitude'] as number) <= north &&
              (b['longitude'] as number) >= west &&
              (b['longitude'] as number) <= east,
          );
        }
        const fs = list(b['facts'], 2, 2);
        factCount += fs.length;
        const kinds = new Set<string>();
        let accessKnown = false;
        for (const fv of fs) {
          const f = object(fv, 'kind state evidenceId unknownReason'),
            kind = enumValue(f['kind'], ['identity-location', 'access']);
          requireValue(!kinds.has(kind));
          kinds.add(kind);
          if (f['state'] === 'known') {
            requireValue(f['unknownReason'] === null);
            const e = ref(f['evidenceId'], kind, poi, date);
            if (kind === 'identity-location')
              requireValue(e['sourceUrl'] === `https://www.wikidata.org/w/index.php?title=${qid}&oldid=${rev}`);
            else accessKnown = true;
          } else {
            requireValue(kind === 'access' && f['state'] === 'unknown' && f['evidenceId'] === null);
            enumValue(f['unknownReason'], ['openingHoursUnknown', 'openingHoursExpiredOrNotApplicable']);
            unknown = true;
          }
        }
        const intervals = list(b['openingIntervals'], 56);
        requireValue(accessKnown ? intervals.length > 0 : intervals.length === 0);
        let end = -1;
        for (const iv of intervals) {
          const it = object(iv, 'day startMinute endMinute');
          requireValue(integer(it['day'], 0, 6) === new Date(date + 'T00:00:00Z').getUTCDay());
          const start = integer(it['startMinute'], 0, 1439),
            finish = integer(it['endMinute'], 1, 1440);
          requireValue(start < finish && start >= end);
          end = finish;
        }
      }
      for (let i = 0; i < es.length; i++) {
        const e = object(
            es[i],
            'edgeId fromPoiId toPoiId minimumMinutes maximumMinutes evidenceId restrictions method rightsBasis modelledWalk',
          ),
          eid = id(e['edgeId']);
        requireValue(!edges.has(eid));
        edges.add(eid);
        requireValue(
          e['fromPoiId'] === (bs[i] as Record<string, unknown>)['poiId'] &&
            e['toPoiId'] === (bs[i + 1] as Record<string, unknown>)['poiId'] &&
            e['method'] === 'modelled' &&
            e['rightsBasis'] === 'odbl',
        );
        text(e['restrictions'], 500);
        ref(e['evidenceId'], 'walking', eid, date);
        const path = object(
          e['modelledWalk'],
          'pathId pathHash pathLengthMillimetres crossingCount fromAnchorNodeId toAnchorNodeId modelVersion calibrationStatus',
        );
        id(path['pathId']);
        hash(path['pathHash']);
        const mm = BigInt(path['pathLengthMillimetres'] === '0' ? '0' : tripRevision(path['pathLengthMillimetres']));
        tripRevision(path['fromAnchorNodeId']);
        tripRevision(path['toAnchorNodeId']);
        requireValue(
          (mm === 0n) === (path['fromAnchorNodeId'] === path['toAnchorNodeId']) &&
            path['modelVersion'] === m['modelVersion'] &&
            path['calibrationStatus'] === 'not-evaluated',
        );
        const crossings = integer(path['crossingCount'], 0, 120),
          min = integer(e['minimumMinutes'], 1, 120),
          max = integer(e['maximumMinutes'], min, 120);
        const lower = (mm + 74999n) / 75000n > 1n ? (mm + 74999n) / 75000n : 1n;
        const upper = (mm + 44999n) / 45000n + BigInt(crossings);
        requireValue(BigInt(min) === lower && BigInt(max) === (upper > lower ? upper : lower));
      }
    }
    requireValue(
      blocks <= 12 &&
        edgeCount <= 48 &&
        factCount <= 96 &&
        used.size === evidence.size &&
        (r['outcome'] === 'empty') === (blocks === 0) &&
        (r['expansionCount'] as number) >= blocks,
    );
    requireValue(covered.every((x) => tags.has(x)) && unmet.every((x) => !tags.has(x)));
    requireValue(
      reasons.includes('unmetInterests') === unmet.length > 0 &&
        reasons.includes('noEligibleStops') === (blocks === 0) &&
        reasons.includes('searchLimited') === r['searchLimited'],
    );
    requireValue(
      coverage === 'filled'
        ? reasons.length === 0 &&
            unmet.length === 0 &&
            !r['searchLimited'] &&
            days.every((d) => (d as { blocks: unknown[] }).blocks.length >= 2)
        : reasons.length > 0,
    );
    requireValue(
      equalSet(uncertainties, [
        'calibrationNotEvaluated',
        'humanQualityNotEvaluated',
        'walkingModelled',
        ...(unknown ? ['openingHoursUnknown'] : []),
      ]),
    );
    return r as unknown as TripItineraryComputed;
  } catch {
    return fail();
  }
}
const problemRules: Record<string, [number, TripItineraryErrorKind]> = {
  'Trips.NotFound': [404, 'not-found'],
  'Trips.Unauthenticated': [401, 'unauthenticated'],
  'Trips.Forbidden': [403, 'forbidden'],
  'Trips.PreconditionRequired': [428, 'precondition-required'],
  'Trips.InvalidInput': [400, 'invalid-request'],
  'Trips.PlanTooLarge': [400, 'invalid-request'],
  'Trips.RevisionMismatch': [412, 'stale'],
  'Trips.ItineraryRequiresEmptyPlan': [409, 'conflict'],
  'Trips.ItineraryBusy': [429, 'busy'],
  'Trips.Unavailable': [503, 'unavailable'],
  'Trips.ItineraryUnavailable': [503, 'unavailable'],
  'Trips.ItineraryDeadline': [504, 'deadline'],
};
export function tripItineraryProblemKind(problem: TripItineraryProblem): TripItineraryErrorKind {
  return problemRules[problem.code]?.[1] ?? fail();
}
export function decodeTripItineraryProblem(raw: TripItineraryRaw, status: number): TripItineraryProblem {
  try {
    const r = object(parseTripItineraryJson(raw, 2048, 4), 'type title status code', 'traceId');
    const code = text(r['code']);
    requireValue(
      r['type'] === 'about:blank' &&
        r['status'] === status &&
        Object.hasOwn(problemRules, code) &&
        problemRules[code][0] === status,
    );
    text(r['title'], 100);
    if (Object.hasOwn(r, 'traceId')) text(r['traceId'], 128);
    return r as TripItineraryProblem;
  } catch {
    return fail();
  }
}
