import type {
  FlightAncillaryCatalog,
  FlightCreationStatus,
  FlightMoney,
  FlightPurchase,
} from './flights-ancillaries.types';
import type { FlightQuoteResponse } from './flights-quote.types';
import { decodeFlightItinerary } from './flights-search.decoder';

export class FlightAncillaryContractError extends Error {
  constructor(field: string) {
    super(`Invalid Flights services response at ${field}`);
    this.name = 'FlightAncillaryContractError';
  }
}
function fail(field: string): never {
  throw new FlightAncillaryContractError(field);
}
function row(value: unknown, field: string): Record<string, unknown> {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) fail(field);
  return value as Record<string, unknown>;
}
function list(value: unknown, maximum: number, field: string): unknown[] {
  if (!Array.isArray(value) || value.length > maximum) fail(field);
  return value;
}
export function ancillaryGuid(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value) &&
    value !== '00000000-0000-0000-0000-000000000000'
  );
}
function text(value: unknown, maximum: number, field: string): void {
  if (
    typeof value !== 'string' ||
    !value.trim() ||
    value.length > maximum ||
    [...value].some((character) => {
      const code = character.charCodeAt(0);
      return (code < 32 && code !== 10) || code === 127;
    })
  )
    fail(field);
}
function optionalText(value: unknown, maximum: number, field: string): void {
  if (value !== null) text(value, maximum, field);
}
function time(value: unknown, field: string): void {
  if (typeof value !== 'string' || !/(Z|[+-]\d{2}:\d{2})$/.test(value) || !Number.isFinite(Date.parse(value)))
    fail(field);
}
function integer(value: unknown, minimum: number, maximum: number, field: string): void {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < minimum || value > maximum) fail(field);
}
export function decodeFlightMoney(value: unknown): FlightMoney {
  const money = row(value, 'money');
  if (
    typeof money['amount'] !== 'string' ||
    !/^\d{1,29}(?:\.\d{1,28})?$/.test(money['amount']) ||
    typeof money['currency'] !== 'string' ||
    !/^[A-Z]{3}$/.test(money['currency'])
  )
    fail('money');
  const [whole, fraction = ''] = money['amount'].split('.');
  const coefficient = BigInt(whole + fraction);
  if (coefficient > 79228162514264337593543950335n) fail('money.precision');
  return value as FlightMoney;
}
/** Exact equality only; payment and final totals remain authoritative on the server. */
export function sameFlightMoney(a: FlightMoney, b: FlightMoney): boolean {
  if (a.currency !== b.currency) return false;
  const canonical = (s: string) => {
    const [whole, fraction = ''] = s.split('.');
    return `${BigInt(whole ?? '0')}.${fraction.replace(/0+$/, '')}`;
  };
  return canonical(a.amount) === canonical(b.amount);
}
function sum(values: FlightMoney[], currency: string): FlightMoney {
  if (values.some((m) => m.currency !== currency)) fail('purchase.lineCurrency');
  const scale = Math.max(0, ...values.map((m) => (m.amount.split('.')[1] ?? '').length));
  const coefficient = values.reduce((total, m) => {
    const [whole, fraction = ''] = m.amount.split('.');
    return total + BigInt((whole ?? '0') + fraction.padEnd(scale, '0'));
  }, 0n);
  const digits = coefficient.toString().padStart(scale + 1, '0');
  return { currency, amount: scale === 0 ? digits : `${digits.slice(0, -scale)}.${digits.slice(-scale)}` };
}
function address(value: unknown, counts?: number[]): string {
  const a = row(value, 'segment');
  integer(a['leg'], 0, 3, 'segment.leg');
  integer(a['segment'], 0, 63, 'segment.segment');
  const leg = a['leg'] as number;
  const segment = a['segment'] as number;
  if (counts && (counts[leg] === undefined || segment >= (counts[leg] ?? 0))) fail('segment.membership');
  return `${leg}:${segment}`;
}
function disclosures(value: unknown): void {
  if (value !== null) for (const d of list(value, 32, 'disclosures')) text(d, 2000, 'disclosures');
}
function limits(value: unknown): void {
  if (value === null) return;
  const baggage = row(value, 'baggage');
  for (const field of ['maximumWeightKg', 'maximumHeightCm', 'maximumDepthCm', 'maximumLengthCm']) {
    const v = baggage[field];
    if (v !== null && (typeof v !== 'number' || !Number.isFinite(v) || v < 0)) fail('baggage');
  }
}
function services(value: unknown, ids?: Set<string>, counts?: number[]): void {
  const seen = new Set<string>();
  for (const entry of list(value, 256, 'services')) {
    const s = row(entry, 'service');
    text(s['selectionKey'], 256, 'selectionKey');
    if (
      seen.has(s['selectionKey'] as string) ||
      !ancillaryGuid(s['bookingPassengerId']) ||
      (ids && !ids.has((s['bookingPassengerId'] as string).toLowerCase())) ||
      (s['kind'] !== 'seat' && s['kind'] !== 'checked-baggage')
    )
      fail('service.membership');
    seen.add(s['selectionKey'] as string);
    integer(s['quantity'], 1, 99, 'quantity');
    decodeFlightMoney(s['lineTotal']);
    const scope = list(s['segments'], 64, 'segments').map((a) => address(a, counts));
    if (scope.length === 0 || new Set(scope).size !== scope.length) fail('service.scope');
    optionalText(s['seatDesignator'], 16, 'seat');
    disclosures(s['disclosures']);
    limits(s['baggage']);
    if (
      s['kind'] === 'seat' &&
      (scope.length !== 1 || s['quantity'] !== 1 || s['seatDesignator'] === null || s['disclosures'] === null)
    )
      fail('seat');
  }
}
export function decodeFlightPurchase(
  value: unknown,
  revision?: string,
  ids?: Set<string>,
  counts?: number[],
): FlightPurchase {
  const p = row(value, 'purchase');
  if (
    !ancillaryGuid(p['quoteRevision']) ||
    (revision && (p['quoteRevision'] as string).toLowerCase() !== revision.toLowerCase()) ||
    p['noticeVersion'] !== 'booking-services-v1'
  )
    fail('purchase.revision');
  const base = decodeFlightMoney(p['baseFare']);
  const extras = decodeFlightMoney(p['extras']);
  const total = decodeFlightMoney(p['total']);
  if (base.currency !== total.currency || extras.currency !== total.currency) fail('purchase.currency');
  services(p['services'], ids, counts);
  time(p['expiresAt'], 'purchase.expiresAt');
  const lines = p['services'] as { lineTotal: FlightMoney }[];
  if (
    !sameFlightMoney(
      sum(
        lines.map((s) => s.lineTotal),
        total.currency,
      ),
      extras,
    ) ||
    !sameFlightMoney(sum([base, extras], total.currency), total)
  )
    fail('purchase.totals');
  return value as FlightPurchase;
}
export function decodeFlightAncillaryCatalog(value: unknown, quote: FlightQuoteResponse): FlightAncillaryCatalog {
  const c = row(value, 'catalog');
  if (
    c['aggregateId'] !== quote.aggregateId ||
    c['quoteRevision'] !== quote.binding.revision ||
    typeof c['seatsUnavailable'] !== 'boolean' ||
    typeof c['unsupportedPricing'] !== 'boolean'
  )
    fail('catalog.binding');
  decodeFlightMoney(c['baseFare']);
  time(c['expiresAt'], 'catalog.expiresAt');
  const ids = new Set(quote.binding.slots.map((s) => s.bookingPassengerId.toLowerCase()));
  const counts = quote.offer.itinerary.slices.map((s) => s.segments.length);
  const known = new Map<string, Record<string, unknown>>();
  for (const item of list(c['services'], 32768, 'catalog.services')) {
    const s = row(item, 'catalog.service');
    text(s['selectionKey'], 256, 'selectionKey');
    const key = s['selectionKey'] as string;
    if (
      known.has(key) ||
      ![null, 'seat', 'checked-baggage'].includes(s['kind'] as string | null) ||
      typeof s['selectable'] !== 'boolean'
    )
      fail('catalog.service');
    known.set(key, s);
    const passengers = list(s['bookingPassengerIds'], 9, 'passengers');
    if (
      passengers.length === 0 ||
      passengers.some((p) => !ancillaryGuid(p) || !ids.has(p.toLowerCase())) ||
      new Set(passengers).size !== passengers.length
    )
      fail('catalog.passengers');
    const scope = list(s['segments'], 64, 'segments').map((a) => address(a, counts));
    if (scope.length === 0 || new Set(scope).size !== scope.length) fail('catalog.scope');
    integer(s['maximumQuantity'], 0, 99, 'maximumQuantity');
    if (s['unitPrice'] !== null) decodeFlightMoney(s['unitPrice']);
    optionalText(s['reason'], 80, 'reason');
    optionalText(s['name'], 200, 'name');
    optionalText(s['seatDesignator'], 16, 'seat');
    optionalText(s['physicalSeat'], 80, 'physicalSeat');
    disclosures(s['disclosures']);
    limits(s['baggage']);
    if (
      s['selectable'] &&
      (s['unitPrice'] === null ||
        passengers.length !== 1 ||
        s['maximumQuantity'] === 0 ||
        s['kind'] === null ||
        (s['unitPrice'] as FlightMoney).currency !== (c['baseFare'] as FlightMoney).currency ||
        (s['kind'] === 'seat' &&
          (scope.length !== 1 ||
            s['maximumQuantity'] !== 1 ||
            s['seatDesignator'] === null ||
            s['physicalSeat'] === null ||
            s['disclosures'] === null)))
    )
      fail('catalog.selectable');
  }
  const allowanceKeys = new Set<string>();
  for (const item of list(c['allowances'], 576, 'allowances')) {
    const a = row(item, 'allowance');
    if (!ancillaryGuid(a['bookingPassengerId']) || !ids.has(a['bookingPassengerId'].toLowerCase()))
      fail('allowance.passenger');
    const key = `${a['bookingPassengerId']}:${address(a['segment'], counts)}`;
    if (allowanceKeys.has(key)) fail('allowance.duplicate');
    allowanceKeys.add(key);
    for (const field of ['checkedQuantity', 'carryOnQuantity']) if (a[field] !== null) integer(a[field], 0, 891, field);
  }
  const mapScopes = new Set<string>();
  for (const item of list(c['seatMaps'], 64, 'maps')) {
    const m = row(item, 'map');
    const scope = address(m['segment'], counts);
    if (mapScopes.has(scope)) fail('map.duplicate');
    mapScopes.add(scope);
    let elements = 0;
    for (const cabin of list(m['cabins'], 16, 'cabins')) {
      const b = row(cabin, 'cabin');
      integer(b['deck'], 0, 1, 'deck');
      text(b['cabin'], 32, 'cabin');
      for (const r of list(b['rows'], 4096, 'rows'))
        for (const section of list(row(r, 'row')['sections'], 4096, 'sections')) {
          for (const item of list(row(section, 'section')['elements'], 4096, 'elements')) {
            if (++elements > 4096) fail('map.size');
            const e = row(item, 'element');
            text(e['kind'], 32, 'element.kind');
            optionalText(e['designator'], 16, 'designator');
            for (const k of list(e['serviceKeys'], 9, 'serviceKeys')) {
              const service = typeof k === 'string' ? known.get(k) : undefined;
              if (
                !service ||
                service['kind'] !== 'seat' ||
                service['seatDesignator'] !== e['designator'] ||
                address((service['segments'] as unknown[])[0], counts) !== scope
              )
                fail('map.service');
            }
          }
        }
    }
  }
  return value as FlightAncillaryCatalog;
}
export function decodeFlightCreationStatus(value: unknown, aggregateId: string): FlightCreationStatus {
  const c = row(value, 'creation');
  if (
    c['aggregateId'] !== aggregateId ||
    !['NotStarted', 'InProgress', 'Matches', 'CreatedWithDifferences', 'NotCreated', 'ManualReviewRequired'].includes(
      c['state'] as string,
    ) ||
    !['OfferQuoted', 'Held', 'Confirmed', 'Ticketed', 'Cancelled', 'Refunded'].includes(c['bookingStatus'] as string)
  )
    fail('creation.state');
  integer(c['passengerCount'], 1, 9, 'passengerCount');
  time(c['observedAt'], 'observedAt');
  const passengerIds = list(c['bookingPassengerIds'], 9, 'bookingPassengerIds');
  if (
    passengerIds.some((id) => !ancillaryGuid(id)) ||
    new Set(passengerIds).size !== passengerIds.length ||
    (passengerIds.length !== c['passengerCount'] &&
      !(passengerIds.length === 0 && c['state'] === 'NotStarted' && c['accepted'] === null))
  )
    fail('creation.passengers');
  const ids = new Set((passengerIds as string[]).map((id) => id.toLowerCase()));
  for (const field of ['canConfirm', 'canCancel', 'canRefresh']) if (typeof c[field] !== 'boolean') fail(field);
  const itinerary = c['itinerary'] === null ? null : decodeFlightItinerary(c['itinerary']);
  const counts = itinerary?.slices.map((s) => s.segments.length);
  if (c['accepted'] !== null) decodeFlightPurchase(c['accepted'], undefined, ids, counts);
  if (c['actual'] !== null) {
    const actual = row(c['actual'], 'actual');
    decodeFlightMoney(actual['total']);
    services(actual['services'], ids, counts);
  }
  if (c['heldUntil'] !== null) time(c['heldUntil'], 'heldUntil');
  if (
    (c['canConfirm'] && (c['bookingStatus'] !== 'Held' || !['Matches', 'NotStarted'].includes(c['state'] as string))) ||
    (c['canCancel'] && ['InProgress', 'ManualReviewRequired', 'NotCreated'].includes(c['state'] as string)) ||
    (['Matches', 'CreatedWithDifferences'].includes(c['state'] as string) && c['actual'] === null)
  )
    fail('creation.actions');
  return { ...(value as FlightCreationStatus), itinerary };
}
