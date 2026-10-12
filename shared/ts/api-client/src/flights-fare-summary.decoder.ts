import type { FlightFareSummary, FlightFareSummaryBinding } from './flights-fare-summary.types';

export const FARE_SUMMARY_CONTRACT_NOTICE =
  'Подробные условия недоступны: ответ не соответствует проверенному предложению.';
const AI_STATUSES = [
  'Disabled',
  'NotRequested',
  'NotAuthenticated',
  'UnsupportedMode',
  'InputUnavailable',
  'Busy',
  'ModelUnavailable',
  'InferenceFailed',
  'InvalidOutput',
  'UsageUnknown',
  'AccountingUnavailable',
  'DeadlineExceeded',
  'BudgetExceeded',
  'SourceStale',
  'FreshnessUnknown',
  'Ready',
];
const GUID = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
function check(ok: boolean): asserts ok {
  if (!ok) throw new Error('Invalid fare summary');
}
function row(v: unknown): Record<string, unknown> {
  check(v !== null && typeof v === 'object' && !Array.isArray(v));
  return v as Record<string, unknown>;
}
function text(v: unknown, max = 2048): v is string {
  return typeof v === 'string' && v.trim().length > 0 && v.length <= max;
}
function guid(v: unknown): boolean {
  return typeof v === 'string' && GUID.test(v) && v !== '00000000-0000-0000-0000-000000000000';
}
function time(v: unknown): boolean {
  return (
    typeof v === 'string' &&
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/i.test(v) &&
    Number.isFinite(Date.parse(v)) &&
    new Date(Date.parse(`${v.slice(0, 10)}T00:00:00Z`)).toISOString().slice(0, 10) === v.slice(0, 10)
  );
}
function integer(v: unknown, max: number): v is number {
  return Number.isSafeInteger(v) && (v as number) >= 0 && (v as number) <= max;
}
function array(v: unknown, max: number): unknown[] {
  check(Array.isArray(v) && v.length <= max);
  return v;
}
function decimalAmount(value: unknown): boolean {
  if (typeof value !== 'string' || !/^(?:0|[1-9]\d{0,28})(?:\.\d{1,28})?$/.test(value)) return false;
  const [whole, fraction = ''] = value.split('.');
  const significantFraction = fraction.replace(/0+$/, '');
  return BigInt(`${whole}${significantFraction}`) <= 79228162514264337593543950335n;
}
function condition(v: unknown): void {
  const c = row(v);
  check(['Unknown', 'Allowed', 'NotAllowed'].includes(c['permission'] as string));
  for (const f of ['permissionReason', 'penaltyReason'])
    check(['Known', 'NotProvided', 'InvalidSupplierData'].includes(c[f] as string));
  check((c['permission'] === 'Unknown') === (c['permissionReason'] !== 'Known'));
  if (c['penalty'] !== null) {
    const p = row(c['penalty']);
    check(decimalAmount(p['amount']) && typeof p['currency'] === 'string' && /^[A-Z]{3}$/.test(p['currency']));
  }
  check((c['penalty'] !== null) === (c['penaltyReason'] === 'Known'));
}
export function decodeFlightFareSummary(value: unknown, binding: FlightFareSummaryBinding): FlightFareSummary {
  check(JSON.stringify(value).length <= 131072);
  const s = row(value);
  check(
    s['schemaVersion'] === 1 &&
      guid(s['aggregateId']) &&
      (s['aggregateId'] as string).toLowerCase() === binding.aggregateId.toLowerCase(),
  );
  check(['Current', 'Stale', 'Unavailable'].includes(s['state'] as string));
  check(['QuotedOffer', 'Historical'].includes(s['provenance'] as string));
  check(s['reason'] === null || text(s['reason'], 64));
  check(time(s['serverObservedAt']));
  for (const f of ['sourceObservedAt', 'offerExpiresAt', 'firstDepartureAt']) check(s[f] === null || time(s[f]));
  for (const f of ['offerId', 'quoteRevision']) check(s[f] === null || guid(s[f]));
  check(
    s['fingerprint'] === null || (typeof s['fingerprint'] === 'string' && /^[0-9a-f]{64}$/i.test(s['fingerprint'])),
  );
  check(s['passengerCount'] === binding.passengerCount && integer(s['passengerCount'], 9) && s['passengerCount'] > 0);
  const counts = array(s['segmentCounts'], 4);
  check(counts.every((n) => integer(n, 64) && n > 0) && counts.reduce<number>((a, n) => a + (n as number), 0) <= 64);
  if (binding.segmentCounts && !(s['state'] === 'Unavailable' && counts.length === 0))
    check(JSON.stringify(counts) === JSON.stringify(binding.segmentCounts));
  check(typeof s['aiAvailable'] === 'boolean' && AI_STATUSES.includes(s['aiStatus'] as string));
  check(['Deterministic', 'AiOrganized'].includes(s['presentation'] as string));
  if (s['presentation'] === 'AiOrganized') check(s['aiStatus'] === 'Ready');
  if (binding.historical) check(s['state'] !== 'Current' && s['provenance'] === 'Historical');
  if (s['state'] !== 'Unavailable') {
    check(guid(s['offerId']) && guid(s['quoteRevision']) && s['fingerprint'] !== null && counts.length > 0);
    if (binding.offerId) check((s['offerId'] as string).toLowerCase() === binding.offerId.toLowerCase());
    if (binding.quoteRevision)
      check((s['quoteRevision'] as string).toLowerCase() === binding.quoteRevision.toLowerCase());
    check(time(s['sourceObservedAt']) && time(s['offerExpiresAt']) && time(s['firstDepartureAt']));
    if (binding.offerExpiresAt) check(Date.parse(s['offerExpiresAt'] as string) === Date.parse(binding.offerExpiresAt));
    if (binding.firstDepartureAt)
      check(Date.parse(s['firstDepartureAt'] as string) === Date.parse(binding.firstDepartureAt));
    check(Date.parse(s['sourceObservedAt'] as string) <= Date.parse(s['serverObservedAt'] as string));
    condition(s['changeBeforeDeparture']);
    condition(s['refundBeforeDeparture']);
    if (s['state'] === 'Current')
      check(
        s['reason'] === null &&
          s['provenance'] === 'QuotedOffer' &&
          Date.parse(s['serverObservedAt'] as string) <
            Math.min(Date.parse(s['offerExpiresAt'] as string), Date.parse(s['firstDepartureAt'] as string)),
      );
    else check(text(s['reason'], 64));
  } else check(s['changeBeforeDeparture'] === null && s['refundBeforeDeparture'] === null);
  const legs = array(s['legs'], 4);
  const seenLegs = new Set<number>();
  for (const item of legs) {
    const l = row(item);
    check(integer(l['leg'], counts.length - 1) && !seenLegs.has(l['leg']));
    seenLegs.add(l['leg']);
    check(l['fareBrandName'] === null || text(l['fareBrandName'], 256));
    condition(l['changeBeforeDeparture']);
  }
  const seen = new Set<string>();
  const passengers = array(s['passengers'], 576);
  for (const item of passengers) {
    const p = row(item);
    check(
      integer(p['leg'], counts.length - 1) &&
        integer(p['segment'], (counts[p['leg']] as number) - 1) &&
        integer(p['adult'], binding.passengerCount - 1),
    );
    const key = `${p['leg']}/${p['segment']}/${p['adult']}`;
    check(!seen.has(key));
    seen.add(key);
    check(p['fareBasisCode'] === null || text(p['fareBasisCode'], 64));
    check(p['cabinClassMarketing'] === null || text(p['cabinClassMarketing'], 256));
    for (const f of ['checkedBaggageQuantity', 'carryOnBaggageQuantity'])
      check(p[f] === null || integer(p[f], 2147483647));
  }
  if (s['state'] !== 'Unavailable')
    check(
      legs.length === counts.length &&
        passengers.length === counts.reduce<number>((a, n) => a + (n as number), 0) * binding.passengerCount,
    );
  for (const item of array(s['sections'], 3)) {
    const section = row(item);
    check(['change', 'refund', 'baggage'].includes(section['kind'] as string));
    for (const item of array(section['lines'], 1024)) {
      const line = row(item);
      check(text(line['scope']) && text(line['text']));
      const ids = array(line['factIds'], 576);
      check(ids.length > 0 && ids.every((id) => text(id, 128)) && new Set(ids).size === ids.length);
    }
  }
  for (const f of ['missingInformation', 'notices']) check(array(s[f], 1024).every((v) => text(v)));
  return value as FlightFareSummary;
}
export function optionalFlightFareSummary(
  value: unknown,
  binding: FlightFareSummaryBinding,
): { summary: FlightFareSummary | null; notice?: string } {
  if (value === undefined || value === null) return { summary: null };
  try {
    return { summary: decodeFlightFareSummary(value, binding) };
  } catch {
    return { summary: null, notice: FARE_SUMMARY_CONTRACT_NOTICE };
  }
}
