import { TripsContractError, tripDate, tripDates, tripMoney, tripRecord, tripText } from './trips.decoder';
import { type TripBrief, tripCurrencies, tripInterests } from './trips.types';
import { tripItineraryReleases } from './trips-itinerary.releases';

function fail(): never {
  throw new TripsContractError();
}
function oneOf<T extends string>(value: unknown, values: readonly T[]): T {
  return typeof value === 'string' && values.includes(value as T) ? (value as T) : fail();
}
function list<T>(value: unknown, limit: number, decode: (item: unknown) => T): T[] {
  if (!Array.isArray(value) || value.length > limit) return fail();
  return value.map(decode);
}
function number(value: unknown, max: number, min = 0): number {
  return typeof value === 'number' && Number.isInteger(value) && value <= max && value >= min ? value : fail();
}
function flag(value: unknown): boolean {
  return typeof value === 'boolean' ? value : fail();
}
function instant(value: unknown, utc = false): string {
  const text = tripText(value, 40);
  if (
    !/^\d{4}-\d\d-\d\dT(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|[+-]\d\d:\d\d)$/.test(text) ||
    !Number.isFinite(Date.parse(text)) ||
    (utc && !/(?:Z|\+00:00)$/.test(text))
  )
    return fail();
  tripDate(text.slice(0, 10));
  return text;
}
function time(value: unknown): string {
  return typeof value === 'string' && /^(?:[01]\d|2[0-3]):[0-5]\d$/.test(value) ? value : fail();
}
export function decodeTripOptionAvailability(value: unknown) {
  const v = tripRecord(value, ['profileId', 'asOfUtc', 'startDate', 'endDate', 'dataMode']);
  const dates = tripDates(tripDate(v['startDate']), tripDate(v['endDate']));
  if (dates.length < 2 || dates.length > 3) return fail();
  return {
    profileId: oneOf(v['profileId'], ['spb-option'] as const),
    dataMode: oneOf(v['dataMode'], ['fictional'] as const),
    asOfUtc: instant(v['asOfUtc'], true),
    startDate: dates[0],
    endDate: dates[dates.length - 1],
  };
}
export type TripOptionAvailability = ReturnType<typeof decodeTripOptionAvailability>;
const unsupportedReasons = [
  'directionUnsupported',
  'dateRangeUnsupported',
  'groupUnsupported',
  'horizonUnsupported',
] as const;
const noMatchReasons = [
  'flightUnavailable',
  'hotelUnavailable',
  'timeIncompatible',
  'budgetExceeded',
  'noWalks',
] as const;
const selectionReasons = [
  'onlyCompatibleOption',
  'moreInterests',
  'moreStops',
  'completeFlightAndStay',
  'lowerComparablePrice',
  'equalByStableIds',
] as const;

function leg(value: unknown) {
  const v = tripRecord(value, ['fromAirport', 'toAirport', 'departsAt', 'arrivesAt']);
  const airport = (a: unknown) => (typeof a === 'string' && /^[A-Z]{3}$/.test(a) ? a : fail());
  const departsAt = instant(v['departsAt']),
    arrivesAt = instant(v['arrivesAt']);
  if (Date.parse(departsAt) >= Date.parse(arrivesAt)) return fail();
  return { fromAirport: airport(v['fromAirport']), toAirport: airport(v['toAirport']), departsAt, arrivesAt };
}
function stop(value: unknown) {
  const v = tripRecord(value, [
    'id',
    'title',
    'start',
    'end',
    'dwellMinutes',
    'incomingEdgeId',
    'walkFromPreviousMinutes',
    'accessHoursKnown',
  ]);
  return {
    id: tripText(v['id'], 100),
    title: tripText(v['title'], 200),
    start: time(v['start']),
    end: time(v['end']),
    dwellMinutes: number(v['dwellMinutes'], 1440, 1),
    incomingEdgeId: v['incomingEdgeId'] === null ? null : tripText(v['incomingEdgeId'], 200),
    walkFromPreviousMinutes: number(v['walkFromPreviousMinutes'], 1440),
    accessHoursKnown: flag(v['accessHoursKnown']),
  };
}
function day(value: unknown) {
  const v = tripRecord(value, ['date', 'windowStart', 'windowEnd', 'stops', 'programmeMinutes', 'emptyReason']);
  const stops = list(v['stops'], 16, stop);
  const windowStart = v['windowStart'] === null ? null : time(v['windowStart']);
  const windowEnd = v['windowEnd'] === null ? null : time(v['windowEnd']);
  const emptyReason =
    v['emptyReason'] === null ? null : oneOf(v['emptyReason'], ['noWalkingWindow', 'noEligibleStops'] as const);
  if (
    (windowStart === null) !== (windowEnd === null) ||
    (stops.length === 0) !== (emptyReason !== null) ||
    (stops.length > 0 && windowStart === null)
  )
    return fail();
  return {
    date: tripDate(v['date']),
    windowStart,
    windowEnd,
    stops,
    programmeMinutes: number(v['programmeMinutes'], 1440),
    emptyReason,
  };
}
function priceLine(value: unknown) {
  const v = tripRecord(value, ['component', 'kind', 'amount', 'currency']);
  return {
    component: oneOf(v['component'], ['flightBase', 'hotelBase', 'includedTax', 'localFee'] as const),
    kind: oneOf(v['kind'], ['travelerMandatory', 'included', 'unknownMandatory', 'supplierNet'] as const),
    amount: v['amount'] === null ? null : tripMoney(v['amount'], true),
    currency: v['currency'] === null ? null : oneOf(v['currency'], tripCurrencies),
  };
}
function tripOption(value: unknown, brief: TripBrief) {
  const v = tripRecord(value, [
    'flight',
    'hotel',
    'days',
    'price',
    'coveredInterests',
    'unmetInterests',
    'selectionReason',
    'arrivalTransferMinutes',
    'departureTransferMinutes',
    'hotelToWalkMinutes',
    'walkToHotelMinutes',
  ]);
  const f = tripRecord(v['flight'], ['id', 'adults', 'outbound', 'inbound']);
  const h = tripRecord(v['hotel'], ['id', 'name', 'adults', 'nights', 'checkInAt', 'checkOutAt']);
  const p = tripRecord(v['price'], [
    'currency',
    'knownSubtotal',
    'flightAndStayComplete',
    'budgetStatus',
    'components',
    'unpricedExpenses',
  ]);
  const dates = tripDates(brief.startDate, brief.endDate);
  const days = list(v['days'], 3, day);
  if (
    days.length !== dates.length ||
    days.some((d, i) => d.date !== dates[i]) ||
    h['adults'] !== brief.adults ||
    f['adults'] !== brief.adults ||
    h['nights'] !== days.length - 1 ||
    p['currency'] !== brief.currency
  )
    return fail();
  const interests = (a: unknown) => list(a, tripInterests.length, (i) => oneOf(i, tripInterests));
  return {
    flight: {
      id: tripText(f['id'], 64),
      adults: number(f['adults'], 2, 1),
      outbound: leg(f['outbound']),
      inbound: leg(f['inbound']),
    },
    hotel: {
      id: tripText(h['id'], 64),
      name: tripText(h['name'], 200),
      adults: number(h['adults'], 2, 1),
      nights: number(h['nights'], 2, 1),
      checkInAt: instant(h['checkInAt']),
      checkOutAt: instant(h['checkOutAt']),
    },
    days,
    coveredInterests: interests(v['coveredInterests']),
    unmetInterests: interests(v['unmetInterests']),
    selectionReason: oneOf(v['selectionReason'], selectionReasons),
    arrivalTransferMinutes: number(v['arrivalTransferMinutes'], 1440),
    departureTransferMinutes: number(v['departureTransferMinutes'], 1440),
    hotelToWalkMinutes: number(v['hotelToWalkMinutes'], 1440),
    walkToHotelMinutes: number(v['walkToHotelMinutes'], 1440),
    price: {
      currency: oneOf(p['currency'], tripCurrencies),
      knownSubtotal: tripMoney(p['knownSubtotal'], true),
      flightAndStayComplete: flag(p['flightAndStayComplete']),
      budgetStatus: oneOf(p['budgetStatus'], ['notSpecified', 'indeterminate'] as const),
      components: list(p['components'], 32, priceLine),
      unpricedExpenses: list(p['unpricedExpenses'], 8, (e) => oneOf(e, ['meals', 'transfers'] as const)),
    },
  };
}
export function decodeTripOptionPreview(value: unknown, brief: TripBrief, discovery: TripOptionAvailability) {
  const v = tripRecord(value, [
    'outcome',
    'reasons',
    'catalogueVersion',
    'policyVersion',
    'asOfUtc',
    'datasetId',
    'datasetVersion',
    'datasetHash',
    'attribution',
    'dataUrl',
    'option',
  ]);
  const asOfUtc = instant(v['asOfUtc'], true),
    release = tripItineraryReleases['saint-petersburg'];
  if (
    Date.parse(asOfUtc) !== Date.parse(discovery.asOfUtc) ||
    v['dataUrl'] !== release.offer ||
    v['datasetId'] !== release.datasetId ||
    v['datasetVersion'] !== release.version ||
    v['datasetHash'] !== release.hash
  )
    return fail();
  const meta = {
    asOfUtc,
    catalogueVersion: oneOf(v['catalogueVersion'], ['A', 'B'] as const),
    policyVersion: tripText(v['policyVersion'], 100),
    attribution: tripText(v['attribution'], 1024),
    dataUrl: release.offer,
    datasetId: release.datasetId,
    datasetVersion: release.version,
  };
  const outcome = oneOf(v['outcome'], ['option', 'noMatch', 'unsupported'] as const);
  if (outcome === 'option') {
    if (!Array.isArray(v['reasons']) || v['reasons'].length > 0) return fail();
    return { ...meta, outcome, option: tripOption(v['option'], brief), reasons: [] };
  }
  const reasons = list(v['reasons'], 8, (r) =>
    outcome === 'unsupported' ? oneOf(r, unsupportedReasons) : oneOf(r, noMatchReasons),
  );
  if (v['option'] !== null || !reasons.length) return fail();
  return { ...meta, outcome, option: null, reasons };
}
export type TripOptionPreview = ReturnType<typeof decodeTripOptionPreview>;
