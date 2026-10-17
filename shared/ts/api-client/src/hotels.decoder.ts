import type {
  HotelExecution,
  HotelOffer,
  HotelOutcome,
  HotelRatesSnapshot,
  HotelReservation,
  HotelSearchSnapshot,
  HotelTerms,
} from './hotels.types';

function fail(): never {
  throw new Error('Invalid Hotels response.');
}
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) fail();
  return value as Record<string, unknown>;
}
function text(value: unknown, max = 4096): string {
  if (typeof value !== 'string' || value.length > max) fail();
  return value;
}
function list(value: unknown, max: number): unknown[] {
  if (!Array.isArray(value) || value.length > max) fail();
  return value;
}
function bool(value: unknown): boolean {
  if (typeof value !== 'boolean') fail();
  return value;
}
function integer(value: unknown, min: number, max: number): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < min || value > max) fail();
  return value;
}
function nullable(value: unknown): string | null {
  return value === null ? null : text(value);
}
export function hotelGuid(value: unknown): string {
  const id = text(value, 36);
  if (!/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(id) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(id)) fail();
  return id;
}
function date(value: unknown): string {
  const result = text(value, 40);
  if (!Number.isFinite(Date.parse(result))) fail();
  return result;
}
function money(value: unknown) {
  const v = object(value),
    amount = text(v['amount'], 40),
    currency = text(v['currency'], 3);
  if (!/^\d+(?:\.\d+)?$/.test(amount) || !/^[A-Z]{3}$/.test(currency)) fail();
  return { amount, currency };
}
function facts(value: unknown) {
  return list(value, 100).map((v) => {
    const f = object(v);
    return { text: text(f['text']), scope: text(f['scope']) };
  });
}
export function decodeHotelTerms(value: unknown): HotelTerms {
  const v = object(value),
    p = object(v['property']),
    d = object(v['dates']),
    o = object(v['occupancy']),
    c = object(v['cancellation']);
  return {
    property: {
      id: text(p['id']),
      name: text(p['name']),
      address: nullable(p['address']),
      rating: nullable(p['rating']),
      ratingSource: nullable(p['ratingSource']),
    },
    room: text(v['room']),
    rateId: text(v['rateId']),
    dates: { checkIn: date(d['checkIn']), checkOut: date(d['checkOut']), nights: integer(d['nights'], 1, 99) },
    occupancy: { adults: integer(o['adults'], 1, 2) },
    total: money(v['total']),
    mandatoryLocalPayments:
      v['mandatoryLocalPayments'] === null ? null : list(v['mandatoryLocalPayments'], 20).map(money),
    board: facts(v['board']),
    cancellation: { kind: text(c['kind']), rules: facts(c['rules']) },
    conditions: facts(v['conditions']),
    eligible: bool(v['eligible']),
    paymentTiming: text(v['paymentTiming']),
  };
}
function offer(value: unknown): HotelOffer {
  const v = object(value);
  return {
    provider: text(v['provider']),
    environment: text(v['environment']),
    selectionKey: text(v['selectionKey']),
    terms: decodeHotelTerms(v['terms']),
    sourceExpiresAt: v['sourceExpiresAt'] === null ? null : date(v['sourceExpiresAt']),
    purchasable: bool(v['purchasable']),
    purchaseUnavailableReason: nullable(v['purchaseUnavailableReason']),
  };
}
export function decodeHotelExecution(value: unknown): HotelExecution {
  const v = object(value),
    kind = text(v['kind']);
  if (!['Unsupported', 'Complete', 'Limited', 'Failed'].includes(kind)) fail();
  const items = list(v['items'], 200).map(offer),
    reason = nullable(v['reason']);
  if ((kind === 'Unsupported' || kind === 'Failed') && (items.length || !reason)) fail();
  return { kind: kind as HotelExecution['kind'], items, reason };
}
export function decodeHotelSearch(value: unknown): HotelSearchSnapshot {
  const v = object(value);
  return {
    id: hotelGuid(v['id']),
    result: decodeHotelExecution(v['result']),
    expiresAt: date(v['expiresAt']),
    statuses: list(v['statuses'], 10).map((s) => {
      const status = object(s),
        parsed = decodeHotelExecution({ ...status, items: [] });
      return { provider: text(status['provider']), kind: parsed.kind, reason: parsed.reason };
    }),
  };
}
export function decodeHotelRates(value: unknown): HotelRatesSnapshot {
  const v = object(value);
  return {
    id: hotelGuid(v['id']),
    searchId: hotelGuid(v['searchId']),
    result: decodeHotelExecution(v['result']),
    expiresAt: date(v['expiresAt']),
  };
}
export function decodeHotelReservation(value: unknown): HotelReservation {
  const v = object(value),
    outcome = text(v['outcome']);
  if (!Object.hasOwn(statusText, outcome)) fail();
  const reviewReasons = list(v['reviewReasons'], 6).map((r) => {
    const reason = text(r);
    if (
      ![
        'Deadline',
        'MissingDetails',
        'TransportUnknown',
        'TermsMismatch',
        'Conflict',
        'ObservationUnavailable',
      ].includes(reason)
    )
      fail();
    return reason;
  });
  const f = v['facts'] === null ? null : object(v['facts']);
  const mismatch = v['firstMismatch'] == null ? null : object(v['firstMismatch']);
  const result: HotelReservation = {
    id: hotelGuid(v['id']),
    quoteRevision: integer(v['quoteRevision'], 1, 2147483647),
    accepted: v['accepted'] === null ? null : decodeHotelTerms(v['accepted']),
    validUntil: date(v['validUntil']),
    attemptId: v['attemptId'] === null ? null : hotelGuid(v['attemptId']),
    outcome: outcome as HotelOutcome,
    reviewReasons,
    everReserved: bool(v['everReserved']),
    active: bool(v['active']),
    facts:
      f === null
        ? null
        : {
            bookingId: text(f['bookingId']),
            hotelReference: nullable(f['hotelReference']),
            actual: f['actual'] === null ? null : decodeHotelTerms(f['actual']),
          },
    createdAt: date(v['createdAt']),
    updatedAt: date(v['updatedAt']),
    privateContentAvailable: bool(v['privateContentAvailable']),
    firstMismatch:
      mismatch === null
        ? null
        : {
            observedAt: date(mismatch['observedAt']),
            actual: decodeHotelTerms(mismatch['actual']),
          },
  };
  if (
    result.active !== ['Confirmed', 'ReservedAwaitingDetails'].includes(result.outcome) ||
    (result.outcome === 'Confirmed' && result.privateContentAvailable && !result.facts?.actual)
  )
    fail();
  return result;
}
const statusText: Record<HotelOutcome, string> = {
  Quoted: 'Проверьте условия перед покупкой',
  Submitted: 'Бронируем',
  Pending: 'Ожидаем ответ поставщика',
  Unknown: 'Результат пока неизвестен',
  ReservedAwaitingDetails: 'Поставщик сообщил о брони, детали уточняются',
  Confirmed: 'Бронь подтверждена',
  NotCreated: 'Бронь не создана',
  Cancelled: 'Бронь отменена поставщиком',
};
export function hotelStatusText(outcome: HotelOutcome): string {
  return statusText[outcome];
}
