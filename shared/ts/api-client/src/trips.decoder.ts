import {
  type CreateTrip,
  type ReplaceTrip,
  type TripBrief,
  type TripCatalogue,
  type TripDay,
  type TripDestination,
  type TripPage,
  type TripPlan,
  type TripWarning,
  tripCities,
  tripCurrencies,
  tripInterests,
  tripPaces,
  tripWarnings,
} from './trips.types';
export class TripsContractError extends Error {
  constructor() {
    super('Invalid Trips contract.');
    this.name = 'TripsContractError';
  }
}
const fail = (): never => {
  throw new TripsContractError();
};
export function tripRecord(value: unknown, keys: string[], optional: string[] = []): Record<string, unknown> {
  if (
    !value ||
    typeof value !== 'object' ||
    Array.isArray(value) ||
    Object.keys(value).some((k) => !keys.includes(k) && !optional.includes(k)) ||
    keys.some((k) => !Object.hasOwn(value, k))
  )
    return fail();
  return value as Record<string, unknown>;
}
function member<T extends string>(v: unknown, values: readonly T[]): T {
  return typeof v === 'string' && values.includes(v as T) ? (v as T) : fail();
}
export function tripGuid(v: unknown): string {
  return typeof v === 'string' &&
    /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v) &&
    v !== '00000000-0000-0000-0000-000000000000'
    ? v.toLowerCase()
    : fail();
}
export function tripText(v: unknown, limit: number, note = false): string {
  if (
    typeof v !== 'string' ||
    (!note && v.trim().length === 0) ||
    [...v].length > limit ||
    /[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/u.test(v) ||
    [...v].some((char) => {
      const code = char.codePointAt(0) ?? 0;
      return (code < 32 || (code >= 127 && code <= 159)) && !(note && code === 10);
    })
  )
    return fail();
  return v;
}
export function tripRevision(v: unknown): string {
  return typeof v === 'string' && /^[1-9]\d{0,18}$/.test(v) && BigInt(v) <= 9223372036854775807n ? v : fail();
}
export function tripMoney(v: unknown, subtotal = false): string {
  if (typeof v !== 'string' || !/^(?:0|[1-9]\d*)(?:\.\d{2})?$/.test(v) || v.length > 16) return fail();
  if (tripMinor(v) > (subtotal ? 112n : 1n) * 99999999999n) return fail();
  return v.includes('.') ? v : v + '.00';
}
export function tripMinor(v: string): bigint {
  const [whole, fraction = '00'] = v.split('.');
  return BigInt(whole) * 100n + BigInt(fraction);
}
export function tripDecimal(v: bigint): string {
  return `${v / 100n}.${(v % 100n).toString().padStart(2, '0')}`;
}
export function tripDate(v: unknown): string {
  if (typeof v !== 'string' || !/^[0-9]{4}-\d{2}-\d{2}$/.test(v) || v.startsWith('0000')) return fail();
  const date = new Date(v + 'T00:00:00Z');
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === v ? v : fail();
}
export function tripDates(start: string, end: string): string[] {
  tripDate(start);
  tripDate(end);
  const span = (Date.parse(end + 'T00:00:00Z') - Date.parse(start + 'T00:00:00Z')) / 86400000;
  if (span < 0 || span > 13) return fail();
  return Array.from({ length: span + 1 }, (_, index) =>
    new Date(Date.parse(start + 'T00:00:00Z') + index * 86400000).toISOString().slice(0, 10),
  );
}
function timestamp(v: unknown): string {
  if (
    typeof v !== 'string' ||
    !/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v) ||
    !Number.isFinite(Date.parse(v))
  )
    return fail();
  tripDate(v.slice(0, 10));
  return v;
}
function nullable<T>(v: unknown, decode: (v: unknown) => T): T | null {
  return v === null ? null : decode(v);
}
function integer(v: unknown, max: number, min = 0): number {
  return typeof v === 'number' && Number.isInteger(v) && v >= min && v <= max ? v : fail();
}
function enumArray<T extends string>(v: unknown, values: readonly T[]): T[] {
  if (!Array.isArray(v) || v.length > values.length || new Set(v).size !== v.length) return fail();
  return v.map((x) => member(x, values));
}
export function tripBrief(v: unknown, write = false): TripBrief {
  const r = tripRecord(
    v,
    [
      'destinationId',
      'startDate',
      'endDate',
      'adults',
      'currency',
      'interests',
      'pace',
      ...(write ? [] : ['originId', 'totalBudget']),
    ],
    write ? ['originId', 'totalBudget'] : [],
  );
  const startDate = tripDate(r['startDate']),
    endDate = tripDate(r['endDate']);
  tripDates(startDate, endDate);
  return {
    destinationId: member(r['destinationId'], tripCities),
    originId: nullable(r['originId'] ?? null, (v) => member(v, tripCities)),
    startDate,
    endDate,
    adults: integer(r['adults'], 9, 1),
    currency: member(r['currency'], tripCurrencies),
    totalBudget: nullable(r['totalBudget'] ?? null, tripMoney),
    interests: enumArray(r['interests'], tripInterests),
    pace: member(r['pace'], tripPaces),
  };
}
function decodeDays(v: unknown, brief: TripBrief, write = false): TripDay[] {
  const dates = tripDates(brief.startDate, brief.endDate);
  if (!Array.isArray(v) || v.length !== dates.length) return fail();
  const ids = new Set<string>();
  return v.map((value, index) => {
    const day = tripRecord(value, ['date', 'blocks']);
    if (tripDate(day['date']) !== dates[index] || !Array.isArray(day['blocks']) || day['blocks'].length > 8)
      return fail();
    return {
      date: dates[index],
      blocks: day['blocks'].map((value) => {
        const b = tripRecord(
          value,
          ['blockId', 'title', ...(write ? [] : ['note', 'startTime', 'endTime', 'estimate'])],
          write ? ['note', 'startTime', 'endTime', 'estimate'] : [],
        );
        const blockId = tripGuid(b['blockId']);
        if (ids.has(blockId)) return fail();
        ids.add(blockId);
        const localTime = (v: unknown): string =>
          typeof v === 'string' && /^(?:[01]\d|2[0-3]):[0-5]\d$/.test(v) ? v : fail();
        const startTime = nullable(b['startTime'] ?? null, localTime),
          endTime = nullable(b['endTime'] ?? null, localTime);
        if ((startTime === null) !== (endTime === null) || (startTime !== null && endTime! <= startTime)) return fail();
        const estimate = nullable(b['estimate'] ?? null, (value) => {
          const e = tripRecord(value, ['amount', 'currency']);
          const currency = member(e['currency'], tripCurrencies);
          if (currency !== brief.currency) return fail();
          return { amount: tripMoney(e['amount']), currency };
        });
        return {
          blockId,
          title: tripText(b['title'], 100),
          note: nullable(b['note'] ?? null, (v) => tripText(v, 500, true)),
          startTime,
          endTime,
          estimate,
        };
      }),
    };
  });
}
export function tripCreate(v: unknown): CreateTrip {
  const r = tripRecord(v, ['title', 'brief']);
  const result = { title: tripText(r['title'], 100), brief: tripBrief(r['brief'], true) };
  tripBound(result);
  return result;
}
export function decodeTripWrite(v: unknown): ReplaceTrip {
  const r = tripRecord(v, ['title', 'brief', 'days']);
  const brief = tripBrief(r['brief'], true);
  const result = { title: tripText(r['title'], 100), brief, days: decodeDays(r['days'], brief, true) };
  tripBound(result);
  return result;
}
// Deliberate whitelist: callers may pass a detail, but no server-owned field enters a write.
export function tripWrite(v: ReplaceTrip): ReplaceTrip {
  return decodeTripWrite({
    title: v.title,
    brief: {
      destinationId: v.brief.destinationId,
      originId: v.brief.originId,
      startDate: v.brief.startDate,
      endDate: v.brief.endDate,
      adults: v.brief.adults,
      currency: v.brief.currency,
      totalBudget: v.brief.totalBudget,
      interests: [...v.brief.interests],
      pace: v.brief.pace,
    },
    days: v.days.map((d) => ({
      date: d.date,
      blocks: d.blocks.map((b) => ({
        blockId: b.blockId,
        title: b.title,
        note: b.note,
        startTime: b.startTime,
        endTime: b.endTime,
        estimate: b.estimate === null ? null : { amount: b.estimate.amount, currency: b.estimate.currency },
      })),
    })),
  });
}
export function tripTotals(plan: ReplaceTrip): { subtotal: string; unknown: number; warnings: TripWarning[] } {
  let sum = 0n,
    unknown = 0,
    overlap = false;
  for (const day of plan.days) {
    for (const block of day.blocks) {
      if (block.estimate) sum += tripMinor(tripMoney(block.estimate.amount));
      else unknown++;
    }
    const times = day.blocks
      .filter((b) => b.startTime !== null)
      .sort((a, b) => a.startTime!.localeCompare(b.startTime!));
    let latest = '';
    for (const block of times) {
      if (block.startTime! < latest) overlap = true;
      if (block.endTime! > latest) latest = block.endTime!;
    }
  }
  const warnings: TripWarning[] = [];
  if (overlap) warnings.push('OverlappingTimes');
  if (plan.brief.totalBudget !== null && sum > tripMinor(tripMoney(plan.brief.totalBudget)))
    warnings.push('KnownEstimateExceedsBudget');
  return { subtotal: tripDecimal(sum), unknown, warnings };
}
function destination(v: unknown): TripDestination {
  const r = tripRecord(v, ['destinationId', 'displayName', 'timeZone', 'catalogueVersion']);
  const id = member(r['destinationId'], tripCities),
    zone = member(r['timeZone'], ['Europe/Moscow', 'Europe/Kaliningrad'] as const);
  if (zone !== (id === 'kaliningrad' ? 'Europe/Kaliningrad' : 'Europe/Moscow')) return fail();
  return {
    destinationId: id,
    displayName: tripText(r['displayName'], 100),
    timeZone: zone,
    catalogueVersion: member(r['catalogueVersion'], ['1']),
  };
}
export function decodeTripPlan(v: unknown, expectedId?: string): TripPlan {
  tripBound(v);
  const r = tripRecord(v, [
    'tripId',
    'revision',
    'title',
    'brief',
    'days',
    'destination',
    'createdAt',
    'updatedAt',
    'knownUserEstimateSubtotal',
    'unknownEstimateCount',
    'warnings',
  ]);
  const brief = tripBrief(r['brief']),
    days = decodeDays(r['days'], brief),
    dest = destination(r['destination']),
    tripId = tripGuid(r['tripId']);
  const result: TripPlan = {
    tripId,
    revision: tripRevision(r['revision']),
    title: tripText(r['title'], 100),
    brief,
    days,
    destination: dest,
    createdAt: timestamp(r['createdAt']),
    updatedAt: timestamp(r['updatedAt']),
    knownUserEstimateSubtotal: tripMoney(r['knownUserEstimateSubtotal'], true),
    unknownEstimateCount: integer(r['unknownEstimateCount'], 112),
    warnings: enumArray(r['warnings'], tripWarnings),
  };
  const totals = tripTotals(result);
  if (
    (expectedId !== undefined && tripId !== tripGuid(expectedId)) ||
    dest.destinationId !== brief.destinationId ||
    Date.parse(result.updatedAt) < Date.parse(result.createdAt) ||
    result.knownUserEstimateSubtotal !== totals.subtotal ||
    result.unknownEstimateCount !== totals.unknown ||
    JSON.stringify(result.warnings) !== JSON.stringify(totals.warnings)
  )
    return fail();
  return result;
}
export function tripCursor(v: unknown): string | null {
  return v === null ? null : typeof v === 'string' && /^[A-Za-z0-9_=-]{1,256}$/.test(v) ? v : fail();
}
export function decodeTripPage(v: unknown): TripPage {
  tripBound(v);
  const r = tripRecord(v, ['items', 'nextCursor']);
  if (!Array.isArray(r['items']) || r['items'].length > 20) return fail();
  const ids = new Set<string>();
  return {
    items: r['items'].map((value) => {
      const x = tripRecord(value, [
        'tripId',
        'revision',
        'title',
        'destinationId',
        'displayName',
        'startDate',
        'endDate',
        'updatedAt',
      ]);
      const tripId = tripGuid(x['tripId']);
      if (ids.has(tripId)) return fail();
      ids.add(tripId);
      const startDate = tripDate(x['startDate']),
        endDate = tripDate(x['endDate']);
      tripDates(startDate, endDate);
      return {
        tripId,
        revision: tripRevision(x['revision']),
        title: tripText(x['title'], 100),
        destinationId: member(x['destinationId'], tripCities),
        displayName: tripText(x['displayName'], 100),
        startDate,
        endDate,
        updatedAt: timestamp(x['updatedAt']),
      };
    }),
    nextCursor: tripCursor(r['nextCursor']),
  };
}
export function decodeTripCatalogue(v: unknown): TripCatalogue {
  tripBound(v, 10240);
  const r = tripRecord(v, ['catalogueVersion', 'items']);
  if (!Array.isArray(r['items']) || r['items'].length < 1 || r['items'].length > 3) return fail();
  const items = r['items'].map(destination);
  if (new Set(items.map((x) => x.destinationId)).size !== items.length) return fail();
  return { catalogueVersion: member(r['catalogueVersion'], ['1']), items };
}
export function tripBound(v: unknown, max = 131072): void {
  const raw = JSON.stringify(v);
  if (typeof raw !== 'string' || new TextEncoder().encode(raw).length > max) fail();
}
export function parseTripsJson(raw: string, max = 131072): unknown {
  if (
    new TextEncoder().encode(raw).length > max ||
    /[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/u.test(raw)
  )
    return fail();
  try {
    return JSON.parse(raw);
  } catch {
    return fail();
  }
}
